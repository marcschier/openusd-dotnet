// Copyright (c) marcschier. Licensed under the MIT License.

// Retained physics world implementation.
//
// Ownership rules enforced here:
// * The world owns every PhysX object it creates (scenes, dispatcher, actors,
//   shapes, materials, cooked meshes, joints) and destroys them in reverse
//   creation order.
// * The world holds one reference on the process runtime that owns the single
//   PxFoundation and PxPhysics instance.
// * The build page and every command, request, and result buffer stay owned by
//   the caller. The library never retains, reallocates, or frees them.

#include "openusd_physx_events.h"
#include "openusd_physx_cuda.h"
#include "openusd_physx_gpu.h"
#include "openusd_physx_page.h"
#include "openusd_physx_runtime.h"
#include "openusd_physx_support.h"
#include "openusd_physx_translate.h"
#include "openusd_physx_vehicle.h"
#include "openusd_physx_world.h"

#include <PxPhysicsAPI.h>
#include <characterkinematic/PxBoxController.h>
#include <characterkinematic/PxCapsuleController.h>
#include <characterkinematic/PxControllerManager.h>
#include <cooking/PxCooking.h>

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstring>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

namespace
{
using namespace physx;
using openusd_physx_support::Guard;
using openusd_physx_support::IsFinite;
using openusd_physx_support::IsUsableRotation;
using openusd_physx_support::WriteError;

constexpr size_t kQueryTouchBuffer = 256;
constexpr PxU32 kContactPointBuffer = 8;

struct WorldActor
{
    uint64_t id = OPENUSD_PHYSX_INVALID_ID;
    PxRigidActor* actor = nullptr;
    uint32_t type = OPENUSD_PHYSX_ACTOR_STATIC;
    uint32_t scene_index = 0;
    openusd_physx_body_state initial{};
    bool sleeping = false;
};

struct WorldJoint
{
    uint64_t id = OPENUSD_PHYSX_INVALID_ID;
    PxJoint* joint = nullptr;
    openusd_physx_joint_desc desc{};
    int32_t actor0_index = -1;
    int32_t actor1_index = -1;
    bool broken = false;
    bool break_pending = false;
};

// One reduced coordinate articulation link. The state is published exactly like
// a rigid body state, so a consumer binds a link back to its prim through the
// same identity path it already uses for an actor.
struct WorldArticulationLink
{
    uint64_t id = OPENUSD_PHYSX_INVALID_ID;
    PxArticulationLink* link = nullptr;
    uint32_t articulation_index = 0;
    openusd_physx_body_state initial{};
};

struct WorldArticulation
{
    uint64_t id = OPENUSD_PHYSX_INVALID_ID;
    PxArticulationReducedCoordinate* articulation = nullptr;
    uint32_t scene_index = 0;
    size_t link_offset = 0;
    size_t link_count = 0;
};

// One character controller plus the pose it started from. A controller is not a
// simulated body: it is moved by explicit commands and by the gravity this
// world integrates for it, so the accumulated fall velocity lives here rather
// than inside PhysX.
struct WorldController
{
    uint64_t id = OPENUSD_PHYSX_INVALID_ID;
    PxController* controller = nullptr;
    uint32_t scene_index = 0;
    uint32_t flags = OPENUSD_PHYSX_CONTROLLER_FLAG_NONE;
    PxVec3 up{0.0F, 1.0F, 0.0F};
    PxVec3 fall_velocity{0.0F, 0.0F, 0.0F};
    PxVec3 last_velocity{0.0F, 0.0F, 0.0F};
    PxVec3 pending_move{0.0F, 0.0F, 0.0F};
    bool has_pending_move = false;
    bool grounded = false;
    openusd_physx_body_state initial{};
};

bool IsMovable(uint32_t type) noexcept
{
    return type == OPENUSD_PHYSX_ACTOR_DYNAMIC || type == OPENUSD_PHYSX_ACTOR_KINEMATIC;
}

// One vehicle plus the driver input the next step applies. The chassis is an
// actor the actor section already declared, so the vehicle only owns the
// simulation state that PhysX keeps outside the rigid body.
struct WorldVehicle
{
    uint64_t id = OPENUSD_PHYSX_INVALID_ID;
    std::unique_ptr<openusd_physx_vehicle::Instance> instance;
    uint32_t scene_index = 0;
    size_t actor_index = 0;
    float throttle = 0.0F;
    float brake = 0.0F;
    float hand_brake = 0.0F;
    float steer = 0.0F;
    float clutch = 0.0F;
    uint32_t gear = 0;
    uint32_t last_gear = 0;
};

// Receives every PhysX simulation event of one world.
//
// Lifetime: the callback is a by-value member of the world, so it is created
// with the world, outlives every scene the world creates, and is destroyed only
// after every scene has been released. A scene therefore can never call back
// into a destroyed callback, and no scene ever holds a callback belonging to a
// different world. The reference below is bound in the world constructor and
// never rebound.
//
// Threading: PhysX invokes these methods from inside PxScene::fetchResults,
// which the world only calls while it already holds its own mutex, so the
// callback writes into the world buffers without any additional lock and
// without any managed transition. There is no callback across the C ABI.
class WorldEventCallback final : public PxSimulationEventCallback
{
public:
    explicit WorldEventCallback(openusd_physx_world& world) noexcept
        : world_(world)
    {
    }

    void onConstraintBreak(PxConstraintInfo* constraints, PxU32 count) override;
    void onWake(PxActor** actors, PxU32 count) override;
    void onSleep(PxActor** actors, PxU32 count) override;
    void onContact(
        const PxContactPairHeader& pair_header,
        const PxContactPair* pairs,
        PxU32 count) override;
    void onTrigger(PxTriggerPair* pairs, PxU32 count) override;
    void onAdvance(
        const PxRigidBody* const* bodies,
        const PxTransform* poses,
        PxU32 count) override;

private:
    openusd_physx_world& world_;
};

// Receives every controller hit of one world. PhysX reports a hit while the
// controller is being moved, which is inside a call this world already holds
// its own mutex for, so the report writes straight into the world event sink.
class WorldControllerHitReport final : public PxUserControllerHitReport
{
public:
    explicit WorldControllerHitReport(openusd_physx_world& world) noexcept
        : world_(world)
    {
    }

    void onShapeHit(const PxControllerShapeHit& hit) override;
    void onControllerHit(const PxControllersHit& hit) override;
    void onObstacleHit(const PxControllerObstacleHit& hit) override;

private:
    openusd_physx_world& world_;
};
}

struct openusd_physx_world
{
    openusd_physx_world() noexcept
        : event_callback(*this)
        , controller_hit_report(*this)
    {
    }

    openusd_physx_runtime::Reference runtime;
    mutable std::mutex mutex;

    uint32_t flags = OPENUSD_PHYSX_WORLD_FLAG_NONE;
    uint32_t worker_thread_count = 0;
    PxDefaultCpuDispatcher* dispatcher = nullptr;
    WorldEventCallback event_callback;
    WorldControllerHitReport controller_hit_report;

    std::vector<PxScene*> scenes;
    std::vector<openusd_physx_scene_desc> scene_descs;
    std::vector<PxMaterial*> materials;
    PxMaterial* default_material = nullptr;
    std::vector<PxConvexMesh*> convex_meshes;
    std::vector<PxTriangleMesh*> triangle_meshes;
    std::vector<PxHeightField*> height_fields;
    std::vector<WorldActor> actors;
    std::vector<WorldJoint> joints;
    std::vector<WorldArticulation> articulations;
    std::vector<WorldArticulationLink> articulation_links;
    std::vector<PxControllerManager*> controller_managers;
    std::vector<WorldController> controllers;
    std::vector<WorldVehicle> vehicles;
    PxConvexMesh* vehicle_sweep_mesh = nullptr;
    // Every CUDA backed object of this world plus the scenes that were actually
    // created on a device. A scene that asked for the device but could not be
    // given one stays a plain CPU scene and every GPU object it owned is
    // skipped individually.
    openusd_physx_gpu::Content gpu;
    std::vector<char> scene_is_gpu;
    uint32_t tendon_count = 0;
    uint32_t mimic_joint_count = 0;
    uint32_t vehicle_wheel_count = 0;
    uint32_t published_vehicle_wheel_count = 0;
    std::vector<uint64_t> shape_ids;
    std::vector<unsigned char> pair_filter_block;
    std::unordered_map<uint64_t, size_t> actor_by_id;
    std::unordered_map<uint64_t, size_t> scene_by_id;
    std::unordered_map<uint64_t, size_t> controller_by_id;
    std::unordered_map<uint64_t, size_t> vehicle_by_id;
    // A reduced coordinate link is not an actor, so it is absent from
    // actor_by_id, but it is addressed by its own prim identity exactly as an
    // actor is. Without this map every command aimed at a link resolves to
    // nothing and is reported as a missing target, which looks identical to a
    // simulation that ignores its input.
    std::unordered_map<uint64_t, size_t> articulation_link_by_id;

    openusd_physx_result_capacities capacities{};
    uint32_t max_substeps = 1;
    double default_time_step = 1.0 / 60.0;
    uint32_t dynamic_actor_count = 0;
    uint64_t revision = 0;
    uint64_t step_index = 0;
    double simulation_time = 0.0;
    double last_step_seconds = 0.0;
    double total_step_seconds = 0.0;
    uint32_t state = OPENUSD_PHYSX_WORLD_STATE_EMPTY;

    openusd_physx_events::EventSink event_sink;
    std::vector<openusd_physx_diagnostic> diagnostics;
    std::vector<openusd_physx_debug_line> debug_lines;
    uint32_t dropped_diagnostics = 0;
    uint32_t dropped_debug_lines = 0;
    uint32_t overflow_flags = OPENUSD_PHYSX_OVERFLOW_NONE;

    std::vector<PxRaycastHit> raycast_scratch;
    std::vector<PxSweepHit> sweep_scratch;
    std::vector<PxOverlapHit> overlap_scratch;
};

namespace
{
void PushDiagnostic(
    openusd_physx_world& world,
    uint32_t severity,
    uint32_t code,
    uint64_t id,
    std::string_view message)
{
    if (world.diagnostics.size() >= world.capacities.max_diagnostics)
    {
        if (world.dropped_diagnostics != UINT32_MAX)
        {
            ++world.dropped_diagnostics;
        }
        world.overflow_flags |= OPENUSD_PHYSX_OVERFLOW_DIAGNOSTICS;
        return;
    }
    openusd_physx_diagnostic diagnostic{};
    diagnostic.id = id;
    diagnostic.severity = severity;
    diagnostic.code = code;
    openusd_physx_support::CopyMessage(diagnostic.message, sizeof(diagnostic.message), message);
    world.diagnostics.push_back(diagnostic);
}

void PushEvent(openusd_physx_world& world, const openusd_physx_event& event)
{
    if ((world.flags & OPENUSD_PHYSX_WORLD_FLAG_ENABLE_EVENTS) == 0)
    {
        return;
    }
    // The sink keeps the deterministic prefix of the total event order and
    // counts everything past the declared capacity. It never allocates here.
    world.event_sink.Retain(event);
    if (world.event_sink.Overflowed())
    {
        world.overflow_flags |= OPENUSD_PHYSX_OVERFLOW_EVENTS;
    }
}

void ClearResultBuffers(openusd_physx_world& world) noexcept
{
    world.event_sink.Reset();
    world.diagnostics.clear();
    world.debug_lines.clear();
    world.dropped_diagnostics = 0;
    world.dropped_debug_lines = 0;
    world.overflow_flags = OPENUSD_PHYSX_OVERFLOW_NONE;
}

// Owns an articulation that is still being built. Releasing an articulation
// releases its links and every shape attached to them, so one guard covers the
// whole partially built tree, and any early exit between creation and the hand
// over to the world releases it exactly once.
class ArticulationScope final
{
public:
    explicit ArticulationScope(PxArticulationReducedCoordinate* articulation) noexcept
        : articulation_(articulation)
    {
    }

    ArticulationScope(const ArticulationScope&) = delete;
    ArticulationScope& operator=(const ArticulationScope&) = delete;
    ArticulationScope(ArticulationScope&&) = delete;
    ArticulationScope& operator=(ArticulationScope&&) = delete;

    ~ArticulationScope()
    {
        if (articulation_ != nullptr)
        {
            const openusd_physx_runtime::FactoryLock factory_lock;
            articulation_->release();
        }
    }

    PxArticulationReducedCoordinate* Get() const noexcept
    {
        return articulation_;
    }

    // Hands ownership to the world once the articulation is complete.
    PxArticulationReducedCoordinate* Detach() noexcept
    {
        PxArticulationReducedCoordinate* held = articulation_;
        articulation_ = nullptr;
        return held;
    }

private:
    PxArticulationReducedCoordinate* articulation_;
};

// Destroys every simulation object owned by the world in reverse creation
// order. The dispatcher and the runtime reference survive so an empty world can
// be rebuilt without recreating the process runtime.
void TeardownContent(openusd_physx_world& world) noexcept
{
    // Releasing shared factory objects is serialized with creation.
    const openusd_physx_runtime::FactoryLock factory_lock;

    // Every CUDA backed object owns device memory that a scene still references,
    // so the GPU content goes first, before the scenes it was added to.
    world.gpu.Teardown();
    world.scene_is_gpu.clear();

    for (WorldJoint& joint : world.joints)
    {
        if (joint.joint != nullptr)
        {
            joint.joint->release();
            joint.joint = nullptr;
        }
    }
    world.joints.clear();

    // Vehicles hold custom constraints on their chassis actor, so they are
    // released before the actors and before the scenes those actors live in.
    for (WorldVehicle& vehicle : world.vehicles)
    {
        if (vehicle.instance != nullptr)
        {
            vehicle.instance->Release();
        }
    }
    world.vehicles.clear();
    world.vehicle_by_id.clear();
    world.vehicle_wheel_count = 0;
    world.published_vehicle_wheel_count = 0;
    if (world.vehicle_sweep_mesh != nullptr)
    {
        openusd_physx_vehicle::DestroySweepMesh(world.vehicle_sweep_mesh);
        world.vehicle_sweep_mesh = nullptr;
    }

    // Controllers must go before their manager, and both must go before the
    // scene, because a controller owns an actor that lives in that scene.
    for (WorldController& controller : world.controllers)
    {
        if (controller.controller != nullptr)
        {
            controller.controller->release();
            controller.controller = nullptr;
        }
    }
    world.controllers.clear();
    world.controller_by_id.clear();

    for (PxControllerManager* manager : world.controller_managers)
    {
        if (manager != nullptr)
        {
            manager->release();
        }
    }
    world.controller_managers.clear();

    // Releasing an articulation releases its links, so the link records only
    // have to forget their pointers.
    for (WorldArticulation& articulation : world.articulations)
    {
        if (articulation.articulation != nullptr)
        {
            if (articulation.articulation->getScene() != nullptr)
            {
                articulation.articulation->getScene()->removeArticulation(*articulation.articulation);
            }
            articulation.articulation->release();
            articulation.articulation = nullptr;
        }
    }
    world.articulations.clear();
    world.articulation_links.clear();
    world.articulation_link_by_id.clear();
    world.tendon_count = 0;
    world.mimic_joint_count = 0;

    for (WorldActor& actor : world.actors)
    {
        if (actor.actor != nullptr)
        {
            if (actor.actor->getScene() != nullptr)
            {
                actor.actor->getScene()->removeActor(*actor.actor);
            }
            actor.actor->release();
            actor.actor = nullptr;
        }
    }
    world.actors.clear();
    world.actor_by_id.clear();
    world.shape_ids.clear();

    for (PxTriangleMesh* mesh : world.triangle_meshes)
    {
        if (mesh != nullptr)
        {
            mesh->release();
        }
    }
    world.triangle_meshes.clear();

    for (PxConvexMesh* mesh : world.convex_meshes)
    {
        if (mesh != nullptr)
        {
            mesh->release();
        }
    }
    world.convex_meshes.clear();

    for (PxHeightField* field : world.height_fields)
    {
        if (field != nullptr)
        {
            field->release();
        }
    }
    world.height_fields.clear();

    for (PxMaterial* material : world.materials)
    {
        if (material != nullptr)
        {
            material->release();
        }
    }
    world.materials.clear();
    if (world.default_material != nullptr)
    {
        world.default_material->release();
        world.default_material = nullptr;
    }

    for (PxScene* scene : world.scenes)
    {
        if (scene != nullptr)
        {
            scene->release();
        }
    }
    world.scenes.clear();
    world.scene_descs.clear();
    world.scene_by_id.clear();
    world.pair_filter_block.clear();

    ClearResultBuffers(world);
    world.capacities = openusd_physx_result_capacities{};
    world.dynamic_actor_count = 0;
    world.max_substeps = 1;
    world.default_time_step = 1.0 / 60.0;
    world.revision = 0;
    world.step_index = 0;
    world.simulation_time = 0.0;
    world.last_step_seconds = 0.0;
    world.total_step_seconds = 0.0;
    world.state = OPENUSD_PHYSX_WORLD_STATE_EMPTY;
}

PxMaterial* ResolveMaterial(openusd_physx_world& world, int32_t material_index) noexcept
{
    if (material_index < 0 || static_cast<size_t>(material_index) >= world.materials.size())
    {
        return world.default_material;
    }
    return world.materials[static_cast<size_t>(material_index)];
}

float ResolveDensity(const openusd_physx_page::View& view, int32_t material_index) noexcept
{
    if (material_index < 0)
    {
        return 1000.0F;
    }
    const openusd_physx_material_desc material =
        view.Get<openusd_physx_material_desc>(view.Header().materials, static_cast<size_t>(material_index));
    return material.density > 0.0F ? material.density : 1000.0F;
}

// Builds the symmetric suppressed pair bit matrix consumed by the filter
// shader. Returns false when the actor count exceeds the supported bound.
bool BuildPairFilterBlock(
    openusd_physx_world& world,
    const openusd_physx_page::View& view,
    std::string& reason)
{
    const openusd_physx_build_page_header& header = view.Header();

    // The block is also what carries the notification selector to the filter
    // shader, so it is built whenever events are enabled even if the page
    // suppresses no pair at all.
    const uint32_t notify_flags = (world.flags & OPENUSD_PHYSX_WORLD_FLAG_ENABLE_EVENTS) != 0
        ? static_cast<uint32_t>(
              openusd_physx_translate::kPairNotifyContacts |
              openusd_physx_translate::kPairNotifyTriggers)
        : static_cast<uint32_t>(openusd_physx_translate::kPairNotifyNone);
    if (header.filter_pairs.count == 0 && notify_flags == openusd_physx_translate::kPairNotifyNone)
    {
        return true;
    }

    // Only the suppressed pair matrix is quadratic in the actor count, so a
    // world that merely turns events on must not inherit that bound.
    const size_t actor_count = header.filter_pairs.count == 0 ? 0 : header.actors.count;
    if (actor_count > openusd_physx_translate::kMaxPairFilterActors)
    {
        reason = "Suppressed collision pairs are limited to " +
            std::to_string(openusd_physx_translate::kMaxPairFilterActors) + " actors.";
        return false;
    }

    const size_t bit_count = actor_count * actor_count;
    const size_t byte_count = sizeof(openusd_physx_translate::PairFilterHeader) + ((bit_count + 7) / 8);
    world.pair_filter_block.assign(byte_count, 0);

    openusd_physx_translate::PairFilterHeader block_header{};
    block_header.actor_count = static_cast<uint32_t>(actor_count);
    block_header.notify_flags = notify_flags;
    std::memcpy(world.pair_filter_block.data(), &block_header, sizeof(block_header));

    for (size_t index = 0; index < header.filter_pairs.count; ++index)
    {
        const openusd_physx_filter_pair pair =
            view.Get<openusd_physx_filter_pair>(header.filter_pairs, index);
        const size_t first = static_cast<size_t>(pair.actor0_index) * actor_count + pair.actor1_index;
        const size_t second = static_cast<size_t>(pair.actor1_index) * actor_count + pair.actor0_index;
        world.pair_filter_block[sizeof(block_header) + (first / 8)] |=
            static_cast<unsigned char>(1U << (first % 8));
        world.pair_filter_block[sizeof(block_header) + (second / 8)] |=
            static_cast<unsigned char>(1U << (second % 8));
    }
    return true;
}

PX_NOINLINE void StoreGeometry(PxGeometryHolder& holder, const PxGeometry& geometry)
{
    holder.storeAny(geometry);
}

bool MakeGeometry(
    openusd_physx_world& world,
    const openusd_physx_page::View& view,
    const openusd_physx_shape_desc& shape,
    size_t shape_index,
    PxGeometryHolder& holder,
    std::string& reason)
{
    const PxVec3 scale = openusd_physx_translate::ToPx(shape.scale);
    const float uniform = std::max(std::max(std::fabs(scale.x), std::fabs(scale.y)), std::fabs(scale.z));
    switch (shape.type)
    {
    case OPENUSD_PHYSX_SHAPE_SPHERE:
        StoreGeometry(holder, PxSphereGeometry(std::max(shape.radius * uniform, 1e-4F)));
        break;
    case OPENUSD_PHYSX_SHAPE_BOX:
        StoreGeometry(holder, PxBoxGeometry(
            std::max(shape.half_extents.x * std::fabs(scale.x), 1e-4F),
            std::max(shape.half_extents.y * std::fabs(scale.y), 1e-4F),
            std::max(shape.half_extents.z * std::fabs(scale.z), 1e-4F)));
        break;
    case OPENUSD_PHYSX_SHAPE_CAPSULE:
        StoreGeometry(holder, PxCapsuleGeometry(
            std::max(shape.radius * uniform, 1e-4F),
            std::max(shape.half_height * uniform, 1e-4F)));
        break;
    case OPENUSD_PHYSX_SHAPE_PLANE:
        StoreGeometry(holder, PxPlaneGeometry());
        break;
    case OPENUSD_PHYSX_SHAPE_CYLINDER:
    case OPENUSD_PHYSX_SHAPE_CONE:
    {
        /* PxConvexCore states both cores about the local X axis, which is what
         * the page promises, so the authored local pose already carries the
         * rotation an authored axis needs. The margin rounds the silhouette,
         * so it is subtracted from the requested extents rather than added to
         * them, otherwise the shape would come out larger than authored. */
        const float radius = std::max(shape.radius * uniform, 1e-4F);
        const float height = std::max(shape.half_height * uniform * 2.0F, 1e-4F);
        const float margin = std::min(radius, height * 0.5F) * 0.05F;
        if (shape.type == OPENUSD_PHYSX_SHAPE_CYLINDER)
        {
            PxConvexCore::Cylinder core;
            core.height = std::max(height - (margin * 2.0F), 1e-4F);
            core.radius = std::max(radius - margin, 1e-4F);
            const PxConvexCoreGeometry geometry(core, margin);
            if (!geometry.isValid())
            {
                reason = "Cylinder geometry for shape " + std::to_string(shape_index) + " is not valid.";
                return false;
            }
            StoreGeometry(holder, geometry);
        }
        else
        {
            PxConvexCore::Cone core;
            core.height = std::max(height - (margin * 2.0F), 1e-4F);
            core.radius = std::max(radius - margin, 1e-4F);
            const PxConvexCoreGeometry geometry(core, margin);
            if (!geometry.isValid())
            {
                reason = "Cone geometry for shape " + std::to_string(shape_index) + " is not valid.";
                return false;
            }
            StoreGeometry(holder, geometry);
        }
        break;
    }
    case OPENUSD_PHYSX_SHAPE_HEIGHTFIELD:
    {
        std::vector<PxHeightFieldSample> samples(
            static_cast<size_t>(shape.row_count) * shape.column_count);
        for (size_t sample = 0; sample < samples.size(); ++sample)
        {
            const openusd_physx_heightfield_sample value = view.Get<openusd_physx_heightfield_sample>(
                view.Header().heightfield_samples,
                static_cast<size_t>(shape.sample_offset) + sample);
            samples[sample].height = value.height;
            samples[sample].materialIndex0 = value.material0;
            samples[sample].materialIndex1 = value.material1;
        }
        PxHeightField* field = openusd_physx_translate::CookHeightField(
            openusd_physx_runtime::Physics(),
            samples.data(),
            shape.row_count,
            shape.column_count);
        if (field == nullptr)
        {
            reason = "Height field cooking failed for shape " + std::to_string(shape_index) + ".";
            return false;
        }
        world.height_fields.push_back(field);
        StoreGeometry(holder, PxHeightFieldGeometry(
            field,
            PxMeshGeometryFlags(),
            std::max(shape.height_scale * std::fabs(scale.y), 1e-4F),
            std::max(shape.row_scale * std::fabs(scale.x), 1e-4F),
            std::max(shape.column_scale * std::fabs(scale.z), 1e-4F)));
        break;
    }
    case OPENUSD_PHYSX_SHAPE_CONVEX_MESH:
    case OPENUSD_PHYSX_SHAPE_TRIANGLE_MESH:
    {
        std::vector<PxVec3> points(shape.point_count);
        for (size_t point = 0; point < shape.point_count; ++point)
        {
            const openusd_physx_vec3f value = view.Get<openusd_physx_vec3f>(
                view.Header().mesh_points,
                static_cast<size_t>(shape.point_offset) + point);
            points[point] = PxVec3(value.x * scale.x, value.y * scale.y, value.z * scale.z);
        }
        if (shape.type == OPENUSD_PHYSX_SHAPE_CONVEX_MESH)
        {
            PxConvexMesh* mesh = openusd_physx_translate::CookConvexMesh(
                openusd_physx_runtime::Physics(),
                openusd_physx_runtime::CookingParams(),
                points.data(),
                points.size());
            if (mesh == nullptr)
            {
                reason = "Convex cooking failed for shape " + std::to_string(shape_index) + ".";
                return false;
            }
            world.convex_meshes.push_back(mesh);
            StoreGeometry(holder, PxConvexMeshGeometry(mesh));
            break;
        }
        std::vector<uint32_t> indices(shape.index_count);
        for (size_t index = 0; index < shape.index_count; ++index)
        {
            indices[index] = view.Get<uint32_t>(
                view.Header().mesh_indices,
                static_cast<size_t>(shape.index_offset) + index);
        }
        PxTriangleMesh* mesh = openusd_physx_translate::CookTriangleMesh(
            openusd_physx_runtime::Physics(),
            openusd_physx_runtime::CookingParams(),
            points.data(),
            points.size(),
            indices.data(),
            indices.size());
        if (mesh == nullptr)
        {
            reason = "Triangle mesh cooking failed for shape " + std::to_string(shape_index) + ".";
            return false;
        }
        world.triangle_meshes.push_back(mesh);
        StoreGeometry(holder, PxTriangleMeshGeometry(mesh));
        break;
    }
    default:
        reason = "Shape " + std::to_string(shape_index) + " has an unsupported type.";
        return false;
    }
    return true;
}

PxJoint* CreateJoint(
    const openusd_physx_joint_desc& desc,
    PxRigidActor* actor0,
    PxRigidActor* actor1,
    std::string& reason)
{
    PxPhysics& physics = openusd_physx_runtime::Physics();
    const PxTransform frame0 = openusd_physx_translate::ToPx(desc.local_frame0);
    const PxTransform frame1 = openusd_physx_translate::ToPx(desc.local_frame1);
    const bool limit = (desc.flags & OPENUSD_PHYSX_JOINT_FLAG_LIMIT_ENABLED) != 0;
    const bool drive = (desc.flags & OPENUSD_PHYSX_JOINT_FLAG_DRIVE_ENABLED) != 0;
    const bool soft_limit = (desc.flags & OPENUSD_PHYSX_JOINT_FLAG_LIMIT_SOFT) != 0;
    const PxSpring limit_spring(desc.limit_stiffness, desc.limit_damping);
    const openusd_physx_runtime::FactoryLock factory_lock;

    /* A hard limit carries a restitution and a bounce threshold, a soft limit
     * carries a spring instead, so the two are built from different pieces of
     * the same record rather than from one shared shape. */
    const auto make_angular_pair = [&](float lower, float upper) {
        if (soft_limit)
        {
            return PxJointAngularLimitPair(lower, upper, limit_spring);
        }
        PxJointAngularLimitPair pair(lower, upper);
        pair.restitution = desc.limit_restitution;
        pair.bounceThreshold = desc.limit_bounce_threshold;
        if (desc.limit_contact_distance > 0.0F)
        {
            pair.stiffness = 0.0F;
            pair.damping = 0.0F;
        }
        return pair;
    };
    const auto make_linear_pair = [&](float lower, float upper) {
        if (soft_limit)
        {
            return PxJointLinearLimitPair(lower, upper, limit_spring);
        }
        PxJointLinearLimitPair pair(physics.getTolerancesScale(), lower, upper);
        pair.restitution = desc.limit_restitution;
        pair.bounceThreshold = desc.limit_bounce_threshold;
        return pair;
    };

    const auto make_cone = [&]() {
        PxJointLimitCone cone = soft_limit
            ? PxJointLimitCone(desc.cone_angle0, desc.cone_angle1, limit_spring)
            : PxJointLimitCone(desc.cone_angle0, desc.cone_angle1);
        if (!soft_limit)
        {
            cone.restitution = desc.limit_restitution;
            cone.bounceThreshold = desc.limit_bounce_threshold;
        }
        return cone;
    };

    /* PhysX gives a revolute joint a velocity only motor and gives a prismatic
     * or a spherical joint no motor at all, so a single axis joint that authors
     * a drive is built as a D6 with exactly that one axis released. That is the
     * only construction that honours the authored stiffness, damping, target
     * position, target velocity, and force limit together, and it never turns
     * an acceleration drive into free spin. */
    const auto make_driven_single_axis = [&]() -> PxJoint* {
        const bool prismatic = desc.type == OPENUSD_PHYSX_JOINT_PRISMATIC;
        const bool spherical = desc.type == OPENUSD_PHYSX_JOINT_SPHERICAL;
        const PxTransform axis0 =
            spherical ? frame0 : openusd_physx_translate::AxisFrame(frame0, desc.axis);
        const PxTransform axis1 =
            spherical ? frame1 : openusd_physx_translate::AxisFrame(frame1, desc.axis);
        PxD6Joint* joint = PxD6JointCreate(physics, actor0, axis0, actor1, axis1);
        if (joint == nullptr)
        {
            return nullptr;
        }
        joint->setMotion(PxD6Axis::eX, PxD6Motion::eLOCKED);
        joint->setMotion(PxD6Axis::eY, PxD6Motion::eLOCKED);
        joint->setMotion(PxD6Axis::eZ, PxD6Motion::eLOCKED);
        joint->setMotion(PxD6Axis::eTWIST, PxD6Motion::eLOCKED);
        joint->setMotion(PxD6Axis::eSWING1, PxD6Motion::eLOCKED);
        joint->setMotion(PxD6Axis::eSWING2, PxD6Motion::eLOCKED);

        const PxD6Motion::Enum released = limit ? PxD6Motion::eLIMITED : PxD6Motion::eFREE;
        PxD6Drive::Enum drive_axis = PxD6Drive::eTWIST;
        if (prismatic)
        {
            joint->setMotion(PxD6Axis::eX, released);
            if (limit)
            {
                joint->setLinearLimit(
                    PxD6Axis::eX, make_linear_pair(desc.lower_limit, desc.upper_limit));
            }
            drive_axis = PxD6Drive::eX;
        }
        else if (spherical)
        {
            joint->setMotion(PxD6Axis::eSWING1, released);
            joint->setMotion(PxD6Axis::eSWING2, released);
            /* A spherical joint has three angular degrees of freedom and only
             * ever limits the swing cone, so the twist stays free even when a
             * drive is authored on the swing axis. Leaving it locked would turn
             * an authored spherical joint into a two axis joint. */
            joint->setMotion(PxD6Axis::eTWIST, PxD6Motion::eFREE);
            if (limit)
            {
                joint->setSwingLimit(make_cone());
            }
            drive_axis = PxD6Drive::eSWING;
        }
        else
        {
            joint->setMotion(PxD6Axis::eTWIST, released);
            if (limit)
            {
                joint->setTwistLimit(make_angular_pair(desc.lower_limit, desc.upper_limit));
            }
            drive_axis = PxD6Drive::eTWIST;
        }

        joint->setDrive(
            drive_axis,
            PxD6JointDrive(
                desc.drive_stiffness,
                desc.drive_damping,
                desc.drive_max_force > 0.0F ? desc.drive_max_force : PX_MAX_F32,
                (desc.flags & OPENUSD_PHYSX_JOINT_FLAG_DRIVE_ACCELERATION) != 0));

        /* The target is one authored number on the joint axis: a distance along
         * the prismatic axis, or an angle about the revolute or swing axis. */
        PxVec3 position(0.0F);
        PxVec3 linear_velocity(0.0F);
        PxVec3 angular_velocity(0.0F);
        PxQuat rotation(PxIdentity);
        if (prismatic)
        {
            position.x = desc.drive_target_position;
            linear_velocity.x = desc.drive_target_velocity;
        }
        else if (spherical)
        {
            rotation = PxQuat(desc.drive_target_position, PxVec3(0.0F, 1.0F, 0.0F));
            angular_velocity.y = desc.drive_target_velocity;
        }
        else
        {
            rotation = PxQuat(desc.drive_target_position, PxVec3(1.0F, 0.0F, 0.0F));
            angular_velocity.x = desc.drive_target_velocity;
        }
        joint->setDrivePosition(PxTransform(position, rotation.getNormalized()));
        joint->setDriveVelocity(linear_velocity, angular_velocity);
        return joint;
    };

    PxJoint* result = nullptr;
    switch (desc.type)
    {
    case OPENUSD_PHYSX_JOINT_FIXED:
        result = PxFixedJointCreate(physics, actor0, frame0, actor1, frame1);
        break;
    case OPENUSD_PHYSX_JOINT_REVOLUTE:
    {
        if (drive)
        {
            result = make_driven_single_axis();
            break;
        }
        PxRevoluteJoint* joint = PxRevoluteJointCreate(
            physics,
            actor0,
            openusd_physx_translate::AxisFrame(frame0, desc.axis),
            actor1,
            openusd_physx_translate::AxisFrame(frame1, desc.axis));
        if (joint != nullptr && limit)
        {
            joint->setLimit(make_angular_pair(desc.lower_limit, desc.upper_limit));
            joint->setRevoluteJointFlag(PxRevoluteJointFlag::eLIMIT_ENABLED, true);
        }
        result = joint;
        break;
    }
    case OPENUSD_PHYSX_JOINT_PRISMATIC:
    {
        if (drive)
        {
            result = make_driven_single_axis();
            break;
        }
        PxPrismaticJoint* joint = PxPrismaticJointCreate(
            physics,
            actor0,
            openusd_physx_translate::AxisFrame(frame0, desc.axis),
            actor1,
            openusd_physx_translate::AxisFrame(frame1, desc.axis));
        if (joint != nullptr && limit)
        {
            joint->setLimit(make_linear_pair(desc.lower_limit, desc.upper_limit));
            joint->setPrismaticJointFlag(PxPrismaticJointFlag::eLIMIT_ENABLED, true);
        }
        result = joint;
        break;
    }
    case OPENUSD_PHYSX_JOINT_SPHERICAL:
    {
        if (drive)
        {
            result = make_driven_single_axis();
            break;
        }
        PxSphericalJoint* joint = PxSphericalJointCreate(physics, actor0, frame0, actor1, frame1);
        if (joint != nullptr && limit)
        {
            joint->setLimitCone(make_cone());
            joint->setSphericalJointFlag(PxSphericalJointFlag::eLIMIT_ENABLED, true);
        }
        result = joint;
        break;
    }
    case OPENUSD_PHYSX_JOINT_DISTANCE:
    {
        PxDistanceJoint* joint = PxDistanceJointCreate(physics, actor0, frame0, actor1, frame1);
        if (joint != nullptr)
        {
            const float minimum = std::max(0.0F, desc.min_distance);
            const float maximum = std::max(minimum, desc.max_distance);
            joint->setMinDistance(minimum);
            joint->setMaxDistance(maximum);
            joint->setDistanceJointFlag(PxDistanceJointFlag::eMIN_DISTANCE_ENABLED, minimum > 0.0F);
            joint->setDistanceJointFlag(PxDistanceJointFlag::eMAX_DISTANCE_ENABLED, maximum < PX_MAX_F32);
            /* A distance joint states its compliance through the same limit
             * spring every other joint uses. */
            if (soft_limit)
            {
                joint->setStiffness(desc.limit_stiffness);
                joint->setDamping(desc.limit_damping);
                joint->setDistanceJointFlag(PxDistanceJointFlag::eSPRING_ENABLED, true);
            }
        }
        result = joint;
        break;
    }
    case OPENUSD_PHYSX_JOINT_D6:
    {
        PxD6Joint* joint = PxD6JointCreate(physics, actor0, frame0, actor1, frame1);
        if (joint == nullptr)
        {
            break;
        }
        static const PxD6Axis::Enum kAxes[OPENUSD_PHYSX_JOINT_AXIS_COUNT] = {
            PxD6Axis::eX,
            PxD6Axis::eY,
            PxD6Axis::eZ,
            PxD6Axis::eTWIST,
            PxD6Axis::eSWING1,
            PxD6Axis::eSWING2};
        static const PxD6Drive::Enum kDrives[OPENUSD_PHYSX_JOINT_AXIS_COUNT] = {
            PxD6Drive::eX,
            PxD6Drive::eY,
            PxD6Drive::eZ,
            PxD6Drive::eTWIST,
            PxD6Drive::eSWING,
            PxD6Drive::eSWING};
        for (uint32_t axis = 0; axis < OPENUSD_PHYSX_JOINT_AXIS_COUNT; ++axis)
        {
            const uint32_t motion = desc.motion[axis];
            joint->setMotion(
                kAxes[axis],
                motion == OPENUSD_PHYSX_JOINT_MOTION_FREE
                    ? PxD6Motion::eFREE
                    : (motion == OPENUSD_PHYSX_JOINT_MOTION_LIMITED ? PxD6Motion::eLIMITED : PxD6Motion::eLOCKED));
        }
        /* PhysX states a linear limit per axis, so each authored linear range
         * goes to its own axis rather than being folded into one. */
        for (uint32_t axis = OPENUSD_PHYSX_JOINT_AXIS_X; axis <= OPENUSD_PHYSX_JOINT_AXIS_Z; ++axis)
        {
            if (desc.motion[axis] != OPENUSD_PHYSX_JOINT_MOTION_LIMITED)
            {
                continue;
            }
            joint->setLinearLimit(
                kAxes[axis],
                make_linear_pair(desc.axis_lower_limit[axis], desc.axis_upper_limit[axis]));
        }
        if (desc.motion[OPENUSD_PHYSX_JOINT_AXIS_TWIST] == OPENUSD_PHYSX_JOINT_MOTION_LIMITED)
        {
            joint->setTwistLimit(make_angular_pair(
                desc.axis_lower_limit[OPENUSD_PHYSX_JOINT_AXIS_TWIST],
                desc.axis_upper_limit[OPENUSD_PHYSX_JOINT_AXIS_TWIST]));
        }
        const bool swing_limited =
            desc.motion[OPENUSD_PHYSX_JOINT_AXIS_SWING1] == OPENUSD_PHYSX_JOINT_MOTION_LIMITED ||
            desc.motion[OPENUSD_PHYSX_JOINT_AXIS_SWING2] == OPENUSD_PHYSX_JOINT_MOTION_LIMITED;
        if (swing_limited)
        {
            /* A swing cone is symmetric, so each swing contributes the larger
             * magnitude of its authored range. */
            const float swing1 = std::max(
                std::fabs(desc.axis_lower_limit[OPENUSD_PHYSX_JOINT_AXIS_SWING1]),
                std::fabs(desc.axis_upper_limit[OPENUSD_PHYSX_JOINT_AXIS_SWING1]));
            const float swing2 = std::max(
                std::fabs(desc.axis_lower_limit[OPENUSD_PHYSX_JOINT_AXIS_SWING2]),
                std::fabs(desc.axis_upper_limit[OPENUSD_PHYSX_JOINT_AXIS_SWING2]));
            PxJointLimitCone cone = soft_limit
                ? PxJointLimitCone(std::max(swing1, 1e-4F), std::max(swing2, 1e-4F), limit_spring)
                : PxJointLimitCone(std::max(swing1, 1e-4F), std::max(swing2, 1e-4F));
            if (!soft_limit)
            {
                cone.restitution = desc.limit_restitution;
                cone.bounceThreshold = desc.limit_bounce_threshold;
            }
            joint->setSwingLimit(cone);
        }
        PxVec3 drive_position(0.0F);
        PxVec3 drive_linear_velocity(0.0F);
        PxVec3 drive_angular_velocity(0.0F);
        bool has_drive_pose = false;
        for (uint32_t axis = 0; axis < OPENUSD_PHYSX_JOINT_AXIS_COUNT; ++axis)
        {
            if ((desc.axis_drive_flags[axis] & OPENUSD_PHYSX_JOINT_DRIVE_FLAG_ENABLED) == 0)
            {
                continue;
            }
            PxD6JointDrive axis_drive(
                desc.axis_drive_stiffness[axis],
                desc.axis_drive_damping[axis],
                desc.axis_drive_max_force[axis] > 0.0F ? desc.axis_drive_max_force[axis] : PX_MAX_F32,
                (desc.axis_drive_flags[axis] & OPENUSD_PHYSX_JOINT_DRIVE_FLAG_ACCELERATION) != 0);
            joint->setDrive(kDrives[axis], axis_drive);
            has_drive_pose = true;
            switch (axis)
            {
            case OPENUSD_PHYSX_JOINT_AXIS_X:
                drive_position.x = desc.axis_drive_target_position[axis];
                drive_linear_velocity.x = desc.axis_drive_target_velocity[axis];
                break;
            case OPENUSD_PHYSX_JOINT_AXIS_Y:
                drive_position.y = desc.axis_drive_target_position[axis];
                drive_linear_velocity.y = desc.axis_drive_target_velocity[axis];
                break;
            case OPENUSD_PHYSX_JOINT_AXIS_Z:
                drive_position.z = desc.axis_drive_target_position[axis];
                drive_linear_velocity.z = desc.axis_drive_target_velocity[axis];
                break;
            case OPENUSD_PHYSX_JOINT_AXIS_TWIST:
                drive_angular_velocity.x = desc.axis_drive_target_velocity[axis];
                break;
            case OPENUSD_PHYSX_JOINT_AXIS_SWING1:
                drive_angular_velocity.y = desc.axis_drive_target_velocity[axis];
                break;
            default:
                drive_angular_velocity.z = desc.axis_drive_target_velocity[axis];
                break;
            }
        }
        if (has_drive_pose)
        {
            /* The angular drive target is a rotation, and the three authored
             * angles are applied in twist, swing1, swing2 order so the result
             * matches the axis order the page states. */
            const PxQuat drive_rotation =
                PxQuat(desc.axis_drive_target_position[OPENUSD_PHYSX_JOINT_AXIS_TWIST], PxVec3(1.0F, 0.0F, 0.0F)) *
                PxQuat(desc.axis_drive_target_position[OPENUSD_PHYSX_JOINT_AXIS_SWING1], PxVec3(0.0F, 1.0F, 0.0F)) *
                PxQuat(desc.axis_drive_target_position[OPENUSD_PHYSX_JOINT_AXIS_SWING2], PxVec3(0.0F, 0.0F, 1.0F));
            joint->setDrivePosition(PxTransform(drive_position, drive_rotation.getNormalized()));
            joint->setDriveVelocity(drive_linear_velocity, drive_angular_velocity);
        }
        result = joint;
        break;
    }
    default:
        reason = "Joint type " + std::to_string(desc.type) + " is not supported by this ABI version.";
        return nullptr;
    }

    if (result == nullptr)
    {
        reason = "PhysX rejected the joint description.";
        return nullptr;
    }
    result->setConstraintFlag(
        PxConstraintFlag::eCOLLISION_ENABLED,
        (desc.flags & OPENUSD_PHYSX_JOINT_FLAG_COLLISION_ENABLED) != 0);
    result->setConstraintFlag(
        PxConstraintFlag::eDRIVE_LIMITS_ARE_FORCES,
        (desc.flags & OPENUSD_PHYSX_JOINT_FLAG_DRIVE_LIMITS_ARE_FORCES) != 0);
    /* Zero is not authored, so the unscaled mass stands, which is what a page
     * that never states a scale means. */
    if (desc.inv_mass_scale0 > 0.0F)
    {
        result->setInvMassScale0(desc.inv_mass_scale0);
    }
    if (desc.inv_inertia_scale0 > 0.0F)
    {
        result->setInvInertiaScale0(desc.inv_inertia_scale0);
    }
    if (desc.inv_mass_scale1 > 0.0F)
    {
        result->setInvMassScale1(desc.inv_mass_scale1);
    }
    if (desc.inv_inertia_scale1 > 0.0F)
    {
        result->setInvInertiaScale1(desc.inv_inertia_scale1);
    }
    if (desc.break_force > 0.0F || desc.break_torque > 0.0F)
    {
        result->setBreakForce(
            desc.break_force > 0.0F ? desc.break_force : PX_MAX_F32,
            desc.break_torque > 0.0F ? desc.break_torque : PX_MAX_F32);
    }
    return result;
}

// A build failure is always hard: the world is emptied so a caller can never
// observe a partially translated scene, and the exact reason is reported.
openusd_physx_status FailBuild(
    openusd_physx_world& world,
    openusd_physx_error_buffer* error,
    const std::string& reason,
    openusd_physx_status status)
{
    std::string message = reason;
    const std::string detail = openusd_physx_runtime::TakeLastError();
    if (!detail.empty())
    {
        message += " PhysX reported: " + detail;
    }
    TeardownContent(world);
    world.state = OPENUSD_PHYSX_WORLD_STATE_FAULTED;
    WriteError(error, message);
    return status;
}

PxShapeFlags MakeShapeFlags(uint32_t flags) noexcept
{
    PxShapeFlags result = PxShapeFlag::eVISUALIZATION | PxShapeFlag::eSCENE_QUERY_SHAPE;
    if ((flags & OPENUSD_PHYSX_SHAPE_FLAG_TRIGGER) != 0)
    {
        result |= PxShapeFlag::eTRIGGER_SHAPE;
        return result;
    }
    if ((flags & OPENUSD_PHYSX_SHAPE_FLAG_DISABLE_COLLISION) == 0)
    {
        result |= PxShapeFlag::eSIMULATION_SHAPE;
    }
    return result;
}

// Actors, articulation links, and character controller actors all live in
// separate vectors but share one PxActor user data slot, so the slot carries a
// kind tag alongside the one based index. The 24 bit index field is far above
// any count the page validator admits.
constexpr uintptr_t kUserDataKindShift = 24;
constexpr uintptr_t kUserDataIndexMask = (uintptr_t{1} << kUserDataKindShift) - 1;
constexpr uintptr_t kUserDataKindActor = 0;
constexpr uintptr_t kUserDataKindLink = 1;
constexpr uintptr_t kUserDataKindController = 2;

void* MakeActorUserData(uintptr_t kind, size_t index) noexcept
{
    return reinterpret_cast<void*>((kind << kUserDataKindShift) | ((static_cast<uintptr_t>(index) + 1) & kUserDataIndexMask));
}

bool DecodeActorUserData(const PxActor* actor, uintptr_t& kind, size_t& index) noexcept
{
    if (actor == nullptr || actor->userData == nullptr)
    {
        return false;
    }
    const uintptr_t raw = reinterpret_cast<uintptr_t>(actor->userData);
    const uintptr_t slot = raw & kUserDataIndexMask;
    if (slot == 0)
    {
        return false;
    }
    kind = raw >> kUserDataKindShift;
    index = static_cast<size_t>(slot) - 1;
    return true;
}

size_t ShapeIndexOf(const PxShape* shape) noexcept
{
    if (shape == nullptr || shape->userData == nullptr)
    {
        return static_cast<size_t>(-1);
    }
    return static_cast<size_t>(reinterpret_cast<uintptr_t>(shape->userData)) - 1;
}

uint64_t ActorIdOf(const openusd_physx_world& world, const PxActor* actor) noexcept
{
    uintptr_t kind = 0;
    size_t index = 0;
    if (!DecodeActorUserData(actor, kind, index))
    {
        return OPENUSD_PHYSX_INVALID_ID;
    }
    if (kind == kUserDataKindActor)
    {
        return index < world.actors.size() ? world.actors[index].id : OPENUSD_PHYSX_INVALID_ID;
    }
    if (kind == kUserDataKindLink)
    {
        return index < world.articulation_links.size()
            ? world.articulation_links[index].id
            : OPENUSD_PHYSX_INVALID_ID;
    }
    if (kind == kUserDataKindController)
    {
        return index < world.controllers.size() ? world.controllers[index].id : OPENUSD_PHYSX_INVALID_ID;
    }
    return OPENUSD_PHYSX_INVALID_ID;
}

uint64_t ShapeIdOf(const openusd_physx_world& world, const PxShape* shape) noexcept
{
    const size_t index = ShapeIndexOf(shape);
    return index < world.shape_ids.size() ? world.shape_ids[index] : OPENUSD_PHYSX_INVALID_ID;
}

// Orders the two sides of a symmetric pair by identity so that the reported
// event never depends on the order PhysX happened to report the pair in. The
// normal is flipped with the pair so it always points from id0 towards id1.
void CanonicalizePair(openusd_physx_event& event) noexcept
{
    const bool swap = event.id1 < event.id0 ||
        (event.id1 == event.id0 && event.detail1 < event.detail0);
    if (!swap)
    {
        return;
    }
    std::swap(event.id0, event.id1);
    std::swap(event.detail0, event.detail1);
    event.normal.x = -event.normal.x;
    event.normal.y = -event.normal.y;
    event.normal.z = -event.normal.z;
}
}

void WorldEventCallback::onConstraintBreak(PxConstraintInfo* constraints, PxU32 count)
{
    if (constraints == nullptr)
    {
        return;
    }
    // The deterministic post substep scan is what emits the joint break event.
    // Marking the record here only makes the break visible immediately, even
    // when PhysX clears the constraint flag before the scan runs.
    for (PxU32 index = 0; index < count; ++index)
    {
        const PxConstraintInfo& info = constraints[index];
        if (info.type != static_cast<PxU32>(PxConstraintExtIDs::eJOINT) ||
            info.externalReference == nullptr)
        {
            continue;
        }
        PxJoint* joint = static_cast<PxJoint*>(info.externalReference);
        const uintptr_t slot = reinterpret_cast<uintptr_t>(joint->userData);
        if (slot == 0 || slot > world_.joints.size())
        {
            continue;
        }
        world_.joints[slot - 1].break_pending = true;
    }
}

void WorldEventCallback::onWake(PxActor** actors, PxU32 count)
{
    // Sleep transitions are emitted by the deterministic post substep scan,
    // which visits actors in build page order. Consuming the unordered PhysX
    // notification here would add nothing and would not be deterministic.
    (void)actors;
    (void)count;
}

void WorldEventCallback::onSleep(PxActor** actors, PxU32 count)
{
    (void)actors;
    (void)count;
}

void WorldEventCallback::onContact(
    const PxContactPairHeader& pair_header,
    const PxContactPair* pairs,
    PxU32 count)
{
    if (pairs == nullptr)
    {
        return;
    }
    if (pair_header.flags.isSet(PxContactPairHeaderFlag::eREMOVED_ACTOR_0) ||
        pair_header.flags.isSet(PxContactPairHeaderFlag::eREMOVED_ACTOR_1))
    {
        return;
    }

    const uint64_t actor0 = ActorIdOf(world_, pair_header.actors[0]);
    const uint64_t actor1 = ActorIdOf(world_, pair_header.actors[1]);
    for (PxU32 index = 0; index < count; ++index)
    {
        const PxContactPair& pair = pairs[index];
        if (pair.flags.isSet(PxContactPairFlag::eREMOVED_SHAPE_0) ||
            pair.flags.isSet(PxContactPairFlag::eREMOVED_SHAPE_1))
        {
            continue;
        }
        const bool found = pair.events.isSet(PxPairFlag::eNOTIFY_TOUCH_FOUND);
        const bool lost = pair.events.isSet(PxPairFlag::eNOTIFY_TOUCH_LOST);
        if (!found && !lost)
        {
            continue;
        }

        openusd_physx_event event{};
        event.id0 = actor0;
        event.id1 = actor1;
        event.detail0 = ShapeIdOf(world_, pair.shapes[0]);
        event.detail1 = ShapeIdOf(world_, pair.shapes[1]);
        event.step_index = world_.step_index;
        event.type = found
            ? static_cast<uint32_t>(OPENUSD_PHYSX_EVENT_CONTACT_FOUND)
            : static_cast<uint32_t>(OPENUSD_PHYSX_EVENT_CONTACT_LOST);
        event.flags = OPENUSD_PHYSX_EVENT_FLAG_DETAIL_IS_SHAPE;

        if (found)
        {
            // A fixed stack buffer keeps contact extraction allocation free.
            // Only the deepest point is reported, which is stable for the pair.
            PxContactPairPoint points[kContactPointBuffer];
            const PxU32 point_count = pair.extractContacts(points, kContactPointBuffer);
            float deepest = 0.0F;
            bool has_point = false;
            PxVec3 impulse(0.0F);
            for (PxU32 point = 0; point < point_count; ++point)
            {
                if (has_point && points[point].separation >= deepest)
                {
                    continue;
                }
                has_point = true;
                deepest = points[point].separation;
                event.position = openusd_physx_translate::FromPx(points[point].position);
                event.normal = openusd_physx_translate::FromPx(points[point].normal);
                impulse = points[point].impulse;
            }
            if (has_point)
            {
                event.flags |= OPENUSD_PHYSX_EVENT_FLAG_HAS_POSITION |
                    OPENUSD_PHYSX_EVENT_FLAG_HAS_NORMAL;
                const float magnitude = impulse.magnitude();
                if (magnitude > 0.0F)
                {
                    event.impulse = magnitude;
                    event.flags |= OPENUSD_PHYSX_EVENT_FLAG_HAS_IMPULSE;
                }
            }
        }

        CanonicalizePair(event);
        PushEvent(world_, event);
    }
}

void WorldEventCallback::onTrigger(PxTriggerPair* pairs, PxU32 count)
{
    if (pairs == nullptr)
    {
        return;
    }
    for (PxU32 index = 0; index < count; ++index)
    {
        const PxTriggerPair& pair = pairs[index];
        if (pair.flags.isSet(PxTriggerPairFlag::eREMOVED_SHAPE_TRIGGER) ||
            pair.flags.isSet(PxTriggerPairFlag::eREMOVED_SHAPE_OTHER))
        {
            continue;
        }

        // The trigger side is always id0, so the pair is never canonicalized:
        // "which volume was entered" is part of the meaning of the event.
        openusd_physx_event event{};
        event.id0 = ActorIdOf(world_, pair.triggerActor);
        event.id1 = ActorIdOf(world_, pair.otherActor);
        event.detail0 = ShapeIdOf(world_, pair.triggerShape);
        event.detail1 = ShapeIdOf(world_, pair.otherShape);
        event.step_index = world_.step_index;
        event.type = pair.status == PxPairFlag::eNOTIFY_TOUCH_FOUND
            ? static_cast<uint32_t>(OPENUSD_PHYSX_EVENT_TRIGGER_ENTER)
            : static_cast<uint32_t>(OPENUSD_PHYSX_EVENT_TRIGGER_LEAVE);
        event.flags = OPENUSD_PHYSX_EVENT_FLAG_DETAIL_IS_SHAPE;
        PushEvent(world_, event);
    }
}

void WorldEventCallback::onAdvance(
    const PxRigidBody* const* bodies,
    const PxTransform* poses,
    PxU32 count)
{
    (void)bodies;
    (void)poses;
    (void)count;
}

namespace
{
// Resolves the identity a controller was built with. The controller user data
// carries the one based slot so the lookup never searches.
uint64_t ControllerIdOf(const openusd_physx_world& world, const PxController* controller) noexcept;
void PushControllerHit(
    openusd_physx_world& world,
    const PxController* controller,
    uint64_t other_id,
    uint64_t detail_id,
    const PxExtendedVec3& world_pos,
    const PxVec3& world_normal);
}

void WorldControllerHitReport::onShapeHit(const PxControllerShapeHit& hit)
{
    PushControllerHit(
        world_,
        hit.controller,
        ActorIdOf(world_, hit.actor),
        ShapeIdOf(world_, hit.shape),
        hit.worldPos,
        hit.worldNormal);
}

void WorldControllerHitReport::onControllerHit(const PxControllersHit& hit)
{
    PushControllerHit(
        world_,
        hit.controller,
        ControllerIdOf(world_, hit.other),
        OPENUSD_PHYSX_INVALID_ID,
        hit.worldPos,
        hit.worldNormal);
}

void WorldControllerHitReport::onObstacleHit(const PxControllerObstacleHit& hit)
{
    PushControllerHit(
        world_,
        hit.controller,
        OPENUSD_PHYSX_INVALID_ID,
        OPENUSD_PHYSX_INVALID_ID,
        hit.worldPos,
        hit.worldNormal);
}

namespace
{
uint64_t ControllerIdOf(const openusd_physx_world& world, const PxController* controller) noexcept
{
    if (controller == nullptr)
    {
        return OPENUSD_PHYSX_INVALID_ID;
    }
    const uintptr_t raw = reinterpret_cast<uintptr_t>(controller->getUserData());
    if (raw == 0)
    {
        return OPENUSD_PHYSX_INVALID_ID;
    }
    const size_t index = static_cast<size_t>(raw) - 1;
    return index < world.controllers.size() ? world.controllers[index].id : OPENUSD_PHYSX_INVALID_ID;
}

void PushControllerHit(
    openusd_physx_world& world,
    const PxController* controller,
    uint64_t other_id,
    uint64_t detail_id,
    const PxExtendedVec3& world_pos,
    const PxVec3& world_normal)
{
    const uint64_t controller_id = ControllerIdOf(world, controller);
    if (controller_id == OPENUSD_PHYSX_INVALID_ID)
    {
        return;
    }
    openusd_physx_event event{};
    event.id0 = controller_id;
    event.id1 = other_id;
    event.detail0 = detail_id;
    event.detail1 = 0;
    event.step_index = world.step_index;
    event.type = static_cast<uint32_t>(OPENUSD_PHYSX_EVENT_CONTROLLER_HIT);
    event.flags = detail_id != OPENUSD_PHYSX_INVALID_ID
        ? static_cast<uint32_t>(OPENUSD_PHYSX_EVENT_FLAG_DETAIL_IS_SHAPE)
        : 0U;
    event.position.x = static_cast<float>(world_pos.x);
    event.position.y = static_cast<float>(world_pos.y);
    event.position.z = static_cast<float>(world_pos.z);
    event.normal = openusd_physx_translate::FromPx(world_normal);
    PushEvent(world, event);
}
}

namespace
{
// Maps the page joint axis order onto the PhysX articulation axis order. The
// two orders differ, so the mapping is spelled out rather than cast.
PxArticulationAxis::Enum ToArticulationAxis(size_t axis) noexcept
{
    switch (axis)
    {
    case OPENUSD_PHYSX_JOINT_AXIS_X:
        return PxArticulationAxis::eX;
    case OPENUSD_PHYSX_JOINT_AXIS_Y:
        return PxArticulationAxis::eY;
    case OPENUSD_PHYSX_JOINT_AXIS_Z:
        return PxArticulationAxis::eZ;
    case OPENUSD_PHYSX_JOINT_AXIS_TWIST:
        return PxArticulationAxis::eTWIST;
    case OPENUSD_PHYSX_JOINT_AXIS_SWING1:
        return PxArticulationAxis::eSWING1;
    default:
        return PxArticulationAxis::eSWING2;
    }
}

PxArticulationMotion::Enum ToArticulationMotion(uint32_t motion) noexcept
{
    switch (motion)
    {
    case OPENUSD_PHYSX_JOINT_MOTION_LIMITED:
        return PxArticulationMotion::eLIMITED;
    case OPENUSD_PHYSX_JOINT_MOTION_FREE:
        return PxArticulationMotion::eFREE;
    default:
        return PxArticulationMotion::eLOCKED;
    }
}

PxArticulationJointType::Enum ToArticulationJointType(uint32_t type) noexcept
{
    switch (type)
    {
    case OPENUSD_PHYSX_ARTICULATION_JOINT_REVOLUTE:
        return PxArticulationJointType::eREVOLUTE;
    case OPENUSD_PHYSX_ARTICULATION_JOINT_PRISMATIC:
        return PxArticulationJointType::ePRISMATIC;
    case OPENUSD_PHYSX_ARTICULATION_JOINT_SPHERICAL:
        return PxArticulationJointType::eSPHERICAL;
    default:
        return PxArticulationJointType::eFIX;
    }
}

PxVec3 UpAxisVector(uint32_t up_axis) noexcept
{
    switch (up_axis)
    {
    case 0:
        return PxVec3(1.0F, 0.0F, 0.0F);
    case 2:
        return PxVec3(0.0F, 0.0F, 1.0F);
    default:
        return PxVec3(0.0F, 1.0F, 0.0F);
    }
}

// Fills the parts of a controller description that do not depend on the shape.
// Every budget keeps the simulation SDK default when the page leaves it at
// zero, so an unauthored controller still behaves like a stock one.
void FillControllerDesc(
    PxControllerDesc& out,
    const openusd_physx_controller_desc& desc,
    const PxVec3& up,
    PxMaterial* material,
    openusd_physx_world& world)
{
    out.position = PxExtendedVec3(
        static_cast<PxExtended>(desc.position.x),
        static_cast<PxExtended>(desc.position.y),
        static_cast<PxExtended>(desc.position.z));
    out.upDirection = up;
    // The page states the slope limit as an angle because that is what a stage
    // authors; PhysX wants its cosine.
    if (desc.slope_limit > 0.0F)
    {
        out.slopeLimit = std::cos(desc.slope_limit);
    }
    if (desc.step_offset > 0.0F)
    {
        out.stepOffset = desc.step_offset;
    }
    if (desc.contact_offset > 0.0F)
    {
        out.contactOffset = desc.contact_offset;
    }
    if (desc.density > 0.0F)
    {
        out.density = desc.density;
    }
    if (desc.scale_coefficient > 0.0F)
    {
        out.scaleCoeff = desc.scale_coefficient;
    }
    if (desc.volume_growth > 0.0F)
    {
        out.volumeGrowth = desc.volume_growth;
    }
    out.nonWalkableMode =
        desc.non_walkable_mode == OPENUSD_PHYSX_CONTROLLER_PREVENT_CLIMBING_AND_FORCE_SLIDING
            ? PxControllerNonWalkableMode::ePREVENT_CLIMBING_AND_FORCE_SLIDING
            : PxControllerNonWalkableMode::ePREVENT_CLIMBING;
    out.material = material;
    out.reportCallback = (desc.flags & OPENUSD_PHYSX_CONTROLLER_FLAG_REPORT_HITS) != 0
        ? &world.controller_hit_report
        : nullptr;
}

openusd_physx_status BuildContent(
    openusd_physx_world& world,
    const openusd_physx_page::View& view,
    openusd_physx_error_buffer* error)
{
    const openusd_physx_build_page_header& header = view.Header();
    PxPhysics& physics = openusd_physx_runtime::Physics();

    // Every material, mesh, scene, actor, shape, and joint below comes from the
    // single process wide factory, so the whole build is serialized against
    // other worlds and against the legacy stage and scene entry points.
    const openusd_physx_runtime::FactoryLock factory_lock;

    world.capacities = header.capacities;
    world.max_substeps = header.max_substeps;
    world.default_time_step = 1.0 / static_cast<double>(header.simulation_rate_hz);
    world.revision = header.revision;

    std::string reason;
    if (!BuildPairFilterBlock(world, view, reason))
    {
        return FailBuild(world, error, reason, OPENUSD_PHYSX_STATUS_UNSUPPORTED);
    }

    world.default_material = physics.createMaterial(0.5F, 0.5F, 0.0F);
    if (world.default_material == nullptr)
    {
        return FailBuild(world, error, "PhysX could not create the default material.", OPENUSD_PHYSX_STATUS_NATIVE_ERROR);
    }

    world.scenes.reserve(header.scenes.count);
    world.scene_descs.reserve(header.scenes.count);
    world.scene_is_gpu.assign(header.scenes.count, 0);

    // A PhysX scene only sweeps bodies when PxSceneFlag::eENABLE_CCD is raised on
    // the scene itself, so an actor that asks for continuous detection has to
    // raise it on the scene it belongs to or its request would be dropped
    // silently. Actor scene indices are validated before the page is built.
    std::vector<char> scene_wants_ccd(header.scenes.count, 0);
    for (size_t index = 0; index < header.actors.count; ++index)
    {
        const openusd_physx_actor_desc actor_desc = view.Get<openusd_physx_actor_desc>(header.actors, index);
        if (actor_desc.type == OPENUSD_PHYSX_ACTOR_DYNAMIC &&
            (actor_desc.flags & OPENUSD_PHYSX_ACTOR_FLAG_ENABLE_CCD) != 0)
        {
            scene_wants_ccd[static_cast<size_t>(actor_desc.scene_index)] = 1;
        }
    }

    for (size_t index = 0; index < header.scenes.count; ++index)
    {
        const openusd_physx_scene_desc desc = view.Get<openusd_physx_scene_desc>(header.scenes, index);
        PxSceneDesc scene_desc(physics.getTolerancesScale());
        scene_desc.gravity = openusd_physx_translate::ToPx(desc.gravity_direction) * desc.gravity_magnitude;
        scene_desc.cpuDispatcher = world.dispatcher;
        scene_desc.filterShader = openusd_physx_translate::WorldFilterShader;
        if (!world.pair_filter_block.empty())
        {
            scene_desc.filterShaderData = world.pair_filter_block.data();
            scene_desc.filterShaderDataSize = static_cast<PxU32>(world.pair_filter_block.size());
        }
        if ((world.flags & OPENUSD_PHYSX_WORLD_FLAG_ENABLE_EVENTS) != 0)
        {
            // The callback is a member of this world, so every scene the world
            // owns reports into the same sink and no scene ever outlives it.
            scene_desc.simulationEventCallback = &world.event_callback;
        }
        if (desc.bounce_threshold > 0.0F)
        {
            scene_desc.bounceThresholdVelocity = desc.bounce_threshold;
        }
        if ((world.flags & OPENUSD_PHYSX_WORLD_FLAG_ENABLE_CCD) != 0 ||
            (desc.flags & OPENUSD_PHYSX_SCENE_FLAG_ENABLE_CCD) != 0 ||
            scene_wants_ccd[index] != 0)
        {
            scene_desc.flags |= PxSceneFlag::eENABLE_CCD;
        }
        if ((world.flags & OPENUSD_PHYSX_WORLD_FLAG_DETERMINISTIC) != 0 ||
            (desc.flags & OPENUSD_PHYSX_SCENE_FLAG_ENABLE_ENHANCED_DETERMINISM) != 0)
        {
            scene_desc.flags |= PxSceneFlag::eENABLE_ENHANCED_DETERMINISM;
        }
        // A scene that owns a CUDA backed object is created on the device. A
        // scene that owns none stays exactly the CPU scene it was before, so
        // enabling the GPU domains never changes how a rigid body only stage
        // simulates.
        if (openusd_physx_gpu::SceneDeclaresGpuContent(view, index))
        {
            std::string gpu_reason;
            if (openusd_physx_gpu::ConfigureScene(scene_desc, gpu_reason) != nullptr)
            {
                world.scene_is_gpu[index] = 1;
            }
        }
        if (!scene_desc.isValid())
        {
            return FailBuild(
                world,
                error,
                "Scene " + std::to_string(index) + " produced an invalid PhysX scene description.",
                OPENUSD_PHYSX_STATUS_INVALID_PAGE);
        }

        PxScene* scene = physics.createScene(scene_desc);
        if (scene == nullptr)
        {
            return FailBuild(
                world,
                error,
                "PhysX could not create scene " + std::to_string(index) + ".",
                OPENUSD_PHYSX_STATUS_NATIVE_ERROR);
        }
        if ((world.flags & OPENUSD_PHYSX_WORLD_FLAG_ENABLE_DEBUG) != 0)
        {
            scene->setVisualizationParameter(PxVisualizationParameter::eSCALE, 1.0F);
            scene->setVisualizationParameter(PxVisualizationParameter::eCOLLISION_SHAPES, 1.0F);
            scene->setVisualizationParameter(PxVisualizationParameter::eACTOR_AXES, 1.0F);
        }
        world.scenes.push_back(scene);
        world.scene_descs.push_back(desc);
        world.scene_by_id.emplace(desc.id, index);
    }

    world.materials.reserve(header.materials.count);
    for (size_t index = 0; index < header.materials.count; ++index)
    {
        const openusd_physx_material_desc desc = view.Get<openusd_physx_material_desc>(header.materials, index);
        PxMaterial* material = physics.createMaterial(desc.static_friction, desc.dynamic_friction, desc.restitution);
        if (material == nullptr)
        {
            return FailBuild(
                world,
                error,
                "PhysX could not create material " + std::to_string(index) + ".",
                OPENUSD_PHYSX_STATUS_NATIVE_ERROR);
        }
        if ((desc.flags & OPENUSD_PHYSX_MATERIAL_FLAG_DISABLE_FRICTION) != 0)
        {
            material->setFlag(PxMaterialFlag::eDISABLE_FRICTION, true);
        }
        if ((desc.flags & OPENUSD_PHYSX_MATERIAL_FLAG_DISABLE_STRONG_FRICTION) != 0)
        {
            material->setFlag(PxMaterialFlag::eDISABLE_STRONG_FRICTION, true);
        }
        if ((desc.flags & OPENUSD_PHYSX_MATERIAL_FLAG_COMPLIANT_CONTACT) != 0)
        {
            /* PhysX reads a compliant contact as a negative restitution that
             * carries the spring stiffness, with the damping alongside it. */
            material->setRestitution(-desc.restitution);
            material->setDamping(desc.damping);
        }
        material->setFrictionCombineMode(static_cast<PxCombineMode::Enum>(desc.friction_combine_mode));
        material->setRestitutionCombineMode(static_cast<PxCombineMode::Enum>(desc.restitution_combine_mode));
        world.materials.push_back(material);
    }

    world.shape_ids.resize(header.shapes.count, OPENUSD_PHYSX_INVALID_ID);
    std::vector<PxGeometryHolder> geometry(header.shapes.count);
    std::vector<char> geometry_ready(header.shapes.count, 0);
    for (size_t index = 0; index < header.shapes.count; ++index)
    {
        world.shape_ids[index] = view.Get<openusd_physx_shape_desc>(header.shapes, index).id;
    }

    // Attaching shapes is identical for a stand alone actor and for an
    // articulation link, and the geometry cache above must be shared by both,
    // so the work lives here once instead of being written twice and drifting.
    // Returns false and fills reason when a shape cannot be built.
    auto attach_shapes = [&](PxRigidActor& target,
                             uint32_t shape_offset,
                             uint32_t shape_count,
                             uint32_t collision_group,
                             size_t owner_index,
                             int32_t scene_index,
                             bool ccd_enabled,
                             float& first_density) -> bool {
        first_density = 1000.0F;
        for (size_t slot = 0; slot < shape_count; ++slot)
        {
            const openusd_physx_actor_shape_ref reference = view.Get<openusd_physx_actor_shape_ref>(
                header.actor_shapes,
                static_cast<size_t>(shape_offset) + slot);
            const size_t shape_index = static_cast<size_t>(reference.shape_index);
            const openusd_physx_shape_desc shape = view.Get<openusd_physx_shape_desc>(header.shapes, shape_index);
            if (geometry_ready[shape_index] == 0)
            {
                reason.clear();
                if (!MakeGeometry(world, view, shape, shape_index, geometry[shape_index], reason))
                {
                    return false;
                }
                geometry_ready[shape_index] = 1;
            }

            const int32_t material_index =
                reference.material_index >= 0 ? reference.material_index : shape.material_index;
            PxMaterial* material = ResolveMaterial(world, material_index);
            if (slot == 0)
            {
                first_density = ResolveDensity(view, material_index);
            }

            PxShape* px_shape = physics.createShape(
                geometry[shape_index].any(),
                *material,
                true,
                MakeShapeFlags(shape.flags));
            if (px_shape == nullptr)
            {
                reason = "PhysX could not create shape " + std::to_string(shape_index) + ".";
                return false;
            }
            px_shape->setLocalPose(openusd_physx_translate::ToPx(shape.local_pose));
            const openusd_physx_scene_desc& scene_desc = world.scene_descs[static_cast<size_t>(scene_index)];
            /* An unauthored offset is zero, and a zero contact offset would
             * stop contact generation entirely, so the scene value stands in
             * for it. The rest offset has no such floor and zero is legal. */
            px_shape->setContactOffset(
                shape.contact_offset > 0.0F ? shape.contact_offset : scene_desc.contact_offset);
            px_shape->setRestOffset(shape.rest_offset);
            if (shape.torsional_patch_radius > 0.0F)
            {
                px_shape->setTorsionalPatchRadius(shape.torsional_patch_radius);
            }
            if (shape.min_torsional_patch_radius > 0.0F)
            {
                px_shape->setMinTorsionalPatchRadius(shape.min_torsional_patch_radius);
            }
            px_shape->userData = reinterpret_cast<void*>(static_cast<uintptr_t>(shape_index + 1));

            PxFilterData filter_data;
            filter_data.word0 = 1U << (collision_group % OPENUSD_PHYSX_MAX_COLLISION_GROUPS);
            filter_data.word1 = 0xFFFFFFFFU;
            filter_data.word2 = static_cast<PxU32>(owner_index);
            filter_data.word3 = ccd_enabled
                ? static_cast<PxU32>(openusd_physx_translate::kFilterWord3Ccd)
                : 0U;
            px_shape->setSimulationFilterData(filter_data);
            px_shape->setQueryFilterData(filter_data);

            if (!target.attachShape(*px_shape))
            {
                /* PhysX refuses a shape a body cannot simulate, and a silently
                 * dropped shape would leave a body colliding with nothing, so
                 * the refusal fails the build with the shape it names. */
                reason = "PhysX refused to attach shape " + std::to_string(shape_index) +
                    " to the actor that references it.";
                px_shape->release();
                return false;
            }
            px_shape->release();
        }
        return true;
    };

    world.actors.reserve(header.actors.count);
    for (size_t index = 0; index < header.actors.count; ++index)
    {
        const openusd_physx_actor_desc desc = view.Get<openusd_physx_actor_desc>(header.actors, index);
        const PxTransform pose = openusd_physx_translate::ToPx(desc.world_pose);
        const bool movable = IsMovable(desc.type);

        PxRigidDynamic* body = nullptr;
        PxRigidActor* actor = nullptr;
        if (movable)
        {
            body = physics.createRigidDynamic(pose);
            actor = body;
        }
        else
        {
            actor = physics.createRigidStatic(pose);
        }
        if (actor == nullptr)
        {
            return FailBuild(
                world,
                error,
                "PhysX could not create actor " + std::to_string(index) + ".",
                OPENUSD_PHYSX_STATUS_NATIVE_ERROR);
        }

        float first_density = 1000.0F;
        // Resolved before the shapes are built because the selector travels in
        // the shape filter data, which is what the filter shader sees. All three
        // levels that can ask for swept contact generation are folded together
        // here: the world, the scene the actor belongs to, and the actor itself.
        // The scene level flag only raises PxSceneFlag::eENABLE_CCD, which by
        // itself neither marks a body nor asks a pair for swept contacts.
        const openusd_physx_scene_desc& actor_scene_desc =
            world.scene_descs[static_cast<size_t>(desc.scene_index)];
        const bool ccd_enabled = desc.type == OPENUSD_PHYSX_ACTOR_DYNAMIC &&
            (((desc.flags & OPENUSD_PHYSX_ACTOR_FLAG_ENABLE_CCD) != 0) ||
             ((actor_scene_desc.flags & OPENUSD_PHYSX_SCENE_FLAG_ENABLE_CCD) != 0) ||
             ((world.flags & OPENUSD_PHYSX_WORLD_FLAG_ENABLE_CCD) != 0));
        if (!attach_shapes(
                *actor,
                desc.shape_offset,
                desc.shape_count,
                desc.collision_group,
                index,
                desc.scene_index,
                ccd_enabled,
                first_density))
        {
            actor->release();
            return FailBuild(world, error, reason, OPENUSD_PHYSX_STATUS_NATIVE_ERROR);
        }

        if (body != nullptr)
        {
            const openusd_physx_scene_desc& scene_desc = world.scene_descs[static_cast<size_t>(desc.scene_index)];
            body->setLinearDamping(desc.linear_damping);
            body->setAngularDamping(desc.angular_damping);
            /* A zero iteration count is not authored, so the owning scene
             * still decides, and only a positive count overrides it. */
            body->setSolverIterationCounts(
                desc.position_iterations > 0 ? desc.position_iterations : scene_desc.position_iterations,
                desc.velocity_iterations > 0 ? desc.velocity_iterations : scene_desc.velocity_iterations);
            if (desc.max_linear_velocity > 0.0F)
            {
                body->setMaxLinearVelocity(desc.max_linear_velocity);
            }
            if (desc.max_angular_velocity > 0.0F)
            {
                body->setMaxAngularVelocity(desc.max_angular_velocity);
            }
            if (desc.max_depenetration_velocity > 0.0F)
            {
                body->setMaxDepenetrationVelocity(desc.max_depenetration_velocity);
            }
            if (desc.max_contact_impulse > 0.0F)
            {
                body->setMaxContactImpulse(desc.max_contact_impulse);
            }
            if (desc.min_ccd_advance_coefficient > 0.0F)
            {
                body->setMinCCDAdvanceCoefficient(desc.min_ccd_advance_coefficient);
            }
            if (desc.contact_slop_coefficient > 0.0F)
            {
                body->setContactSlopCoefficient(desc.contact_slop_coefficient);
            }
            if (desc.stabilization_threshold > 0.0F)
            {
                body->setStabilizationThreshold(desc.stabilization_threshold);
            }
            if (desc.wake_counter > 0.0F)
            {
                body->setWakeCounter(desc.wake_counter);
            }
            if ((desc.flags & OPENUSD_PHYSX_ACTOR_FLAG_DISABLE_GRAVITY) != 0)
            {
                body->setActorFlag(PxActorFlag::eDISABLE_GRAVITY, true);
            }
            PxRigidDynamicLockFlags lock_flags = PxRigidDynamicLockFlags();
            if ((desc.flags & OPENUSD_PHYSX_ACTOR_FLAG_LOCK_ROTATION) != 0)
            {
                lock_flags |= PxRigidDynamicLockFlag::eLOCK_ANGULAR_X |
                    PxRigidDynamicLockFlag::eLOCK_ANGULAR_Y |
                    PxRigidDynamicLockFlag::eLOCK_ANGULAR_Z;
            }
            if ((desc.flags & OPENUSD_PHYSX_ACTOR_FLAG_LOCK_LINEAR_X) != 0)
            {
                lock_flags |= PxRigidDynamicLockFlag::eLOCK_LINEAR_X;
            }
            if ((desc.flags & OPENUSD_PHYSX_ACTOR_FLAG_LOCK_LINEAR_Y) != 0)
            {
                lock_flags |= PxRigidDynamicLockFlag::eLOCK_LINEAR_Y;
            }
            if ((desc.flags & OPENUSD_PHYSX_ACTOR_FLAG_LOCK_LINEAR_Z) != 0)
            {
                lock_flags |= PxRigidDynamicLockFlag::eLOCK_LINEAR_Z;
            }
            if ((desc.flags & OPENUSD_PHYSX_ACTOR_FLAG_LOCK_ANGULAR_X) != 0)
            {
                lock_flags |= PxRigidDynamicLockFlag::eLOCK_ANGULAR_X;
            }
            if ((desc.flags & OPENUSD_PHYSX_ACTOR_FLAG_LOCK_ANGULAR_Y) != 0)
            {
                lock_flags |= PxRigidDynamicLockFlag::eLOCK_ANGULAR_Y;
            }
            if ((desc.flags & OPENUSD_PHYSX_ACTOR_FLAG_LOCK_ANGULAR_Z) != 0)
            {
                lock_flags |= PxRigidDynamicLockFlag::eLOCK_ANGULAR_Z;
            }
            if (lock_flags != PxRigidDynamicLockFlags())
            {
                body->setRigidDynamicLockFlags(lock_flags);
            }
            if ((desc.flags & OPENUSD_PHYSX_ACTOR_FLAG_RETAIN_ACCELERATIONS) != 0)
            {
                body->setRigidBodyFlag(PxRigidBodyFlag::eRETAIN_ACCELERATIONS, true);
            }
            if ((desc.flags & OPENUSD_PHYSX_ACTOR_FLAG_ENABLE_GYROSCOPIC_FORCES) != 0)
            {
                body->setRigidBodyFlag(PxRigidBodyFlag::eENABLE_GYROSCOPIC_FORCES, true);
            }
            if ((desc.flags & OPENUSD_PHYSX_ACTOR_FLAG_ENABLE_SPECULATIVE_CCD) != 0)
            {
                body->setRigidBodyFlag(PxRigidBodyFlag::eENABLE_SPECULATIVE_CCD, true);
            }
            /* A body that must never sleep and a body with an authored
             * threshold both state a threshold, and the per body value wins so
             * that one body can opt out of a scene wide rule. */
            if ((desc.flags & OPENUSD_PHYSX_ACTOR_FLAG_DISABLE_SLEEPING) != 0 ||
                (scene_desc.flags & OPENUSD_PHYSX_SCENE_FLAG_DISABLE_SLEEPING) != 0)
            {
                body->setSleepThreshold(0.0F);
            }
            if (desc.sleep_threshold > 0.0F)
            {
                body->setSleepThreshold(desc.sleep_threshold);
            }

            const PxVec3 center_of_mass = openusd_physx_translate::ToPx(desc.center_of_mass);
            const PxVec3 inertia = openusd_physx_translate::ToPx(desc.inertia);

            // A diagonal inertia is stated about the principal axes, so the mass frame carries
            // that rotation. The page contract accepts any finite quaternion whose length stays
            // near one, so the same rule decides here: an unset or rejected rotation becomes the
            // identity, which is exactly the frame a page without authored principal axes
            // describes, and every accepted rotation keeps its orientation and is normalized.
            const PxQuat principal_axes = openusd_physx_translate::ToPx(
                openusd_physx_support::ResolveRotationOrIdentity(desc.principal_axes));
            const PxTransform mass_frame(center_of_mass, principal_axes);
            if (desc.mass > 0.0F)
            {
                if (inertia.x > 0.0F && inertia.y > 0.0F && inertia.z > 0.0F)
                {
                    body->setCMassLocalPose(mass_frame);
                    body->setMass(desc.mass);
                    body->setMassSpaceInertiaTensor(inertia);
                }
                else
                {
                    // The shapes decide the inertia here, so PhysX computes the mass frame
                    // itself and an authored principal axis frame has nothing to state.
                    PxRigidBodyExt::setMassAndUpdateInertia(*body, desc.mass, &center_of_mass);
                }
            }
            else if (desc.shape_count > 0)
            {
                PxRigidBodyExt::updateMassAndInertia(*body, first_density, &center_of_mass);
            }
            else
            {
                body->setCMassLocalPose(mass_frame);
                body->setMass(1.0F);
                body->setMassSpaceInertiaTensor(PxVec3(1.0F, 1.0F, 1.0F));
            }

            if (desc.type == OPENUSD_PHYSX_ACTOR_KINEMATIC)
            {
                body->setRigidBodyFlag(PxRigidBodyFlag::eKINEMATIC, true);
            }
            else
            {
                if (ccd_enabled)
                {
                    body->setRigidBodyFlag(PxRigidBodyFlag::eENABLE_CCD, true);
                }
                body->setLinearVelocity(openusd_physx_translate::ToPx(desc.linear_velocity));
                body->setAngularVelocity(openusd_physx_translate::ToPx(desc.angular_velocity));
            }
        }

        actor->userData = MakeActorUserData(kUserDataKindActor, index);
        world.scenes[static_cast<size_t>(desc.scene_index)]->addActor(*actor);

        WorldActor record;
        record.id = desc.id;
        record.actor = actor;
        record.type = desc.type;
        record.scene_index = static_cast<uint32_t>(desc.scene_index);
        record.initial.id = desc.id;
        record.initial.pose = desc.world_pose;
        record.initial.linear_velocity = desc.type == OPENUSD_PHYSX_ACTOR_DYNAMIC
            ? desc.linear_velocity
            : openusd_physx_vec3f{0.0F, 0.0F, 0.0F};
        record.initial.angular_velocity = desc.type == OPENUSD_PHYSX_ACTOR_DYNAMIC
            ? desc.angular_velocity
            : openusd_physx_vec3f{0.0F, 0.0F, 0.0F};
        record.initial.flags = 0;
        if (desc.type == OPENUSD_PHYSX_ACTOR_KINEMATIC)
        {
            record.initial.flags |= OPENUSD_PHYSX_BODY_STATE_FLAG_KINEMATIC;
        }
        if (body != nullptr && desc.type == OPENUSD_PHYSX_ACTOR_DYNAMIC &&
            (desc.flags & OPENUSD_PHYSX_ACTOR_FLAG_START_ASLEEP) != 0)
        {
            body->putToSleep();
            record.initial.flags |= OPENUSD_PHYSX_BODY_STATE_FLAG_SLEEPING;
            record.sleeping = true;
        }
        world.actor_by_id.emplace(record.id, world.actors.size());
        world.actors.push_back(record);
        if (movable)
        {
            ++world.dynamic_actor_count;
        }
    }

    world.joints.reserve(header.joints.count);
    for (size_t index = 0; index < header.joints.count; ++index)
    {
        const openusd_physx_joint_desc desc = view.Get<openusd_physx_joint_desc>(header.joints, index);
        WorldJoint record;
        record.id = desc.id;
        record.desc = desc;
        record.actor0_index = desc.actor0_index;
        record.actor1_index = desc.actor1_index;
        if ((desc.flags & OPENUSD_PHYSX_JOINT_FLAG_DISABLED) == 0)
        {
            PxRigidActor* actor0 = desc.actor0_index >= 0
                ? world.actors[static_cast<size_t>(desc.actor0_index)].actor
                : nullptr;
            PxRigidActor* actor1 = desc.actor1_index >= 0
                ? world.actors[static_cast<size_t>(desc.actor1_index)].actor
                : nullptr;
            reason.clear();
            record.joint = CreateJoint(desc, actor0, actor1, reason);
            if (record.joint == nullptr)
            {
                return FailBuild(
                    world,
                    error,
                    "Joint " + std::to_string(index) + " could not be created. " + reason,
                    desc.type == OPENUSD_PHYSX_JOINT_D6
                        ? OPENUSD_PHYSX_STATUS_UNSUPPORTED
                        : OPENUSD_PHYSX_STATUS_NATIVE_ERROR);
            }
            // The slot lets the simulation event callback map a broken
            // constraint back to this record without searching.
            record.joint->userData =
                reinterpret_cast<void*>(static_cast<uintptr_t>(world.joints.size() + 1));
        }
        world.joints.push_back(record);
    }

    // -----------------------------------------------------------------------
    // Reduced coordinate articulations.
    //
    // The link section is one contiguous window per articulation, and the page
    // validator has already proven every parent names a link earlier in the
    // same window, so a single forward pass builds the whole tree and a cycle
    // is unrepresentable rather than merely rejected.
    // -----------------------------------------------------------------------
    world.articulation_links.reserve(header.articulation_links.count);
    world.articulations.reserve(header.articulations.count);
    for (size_t index = 0; index < header.articulations.count; ++index)
    {
        const openusd_physx_articulation_desc desc =
            view.Get<openusd_physx_articulation_desc>(header.articulations, index);
        const openusd_physx_scene_desc& scene_desc =
            world.scene_descs[static_cast<size_t>(desc.scene_index)];

        PxArticulationReducedCoordinate* articulation = physics.createArticulationReducedCoordinate();
        if (articulation == nullptr)
        {
            return FailBuild(
                world,
                error,
                "PhysX could not create articulation " + std::to_string(index) + ".",
                OPENUSD_PHYSX_STATUS_NATIVE_ERROR);
        }
        // Everything below can fail, and until the world takes ownership at the
        // end of the iteration nothing else can see the articulation, so the
        // guard is the only thing that can free it.
        ArticulationScope articulation_scope(articulation);

        articulation->setArticulationFlag(
            PxArticulationFlag::eFIX_BASE,
            (desc.flags & OPENUSD_PHYSX_ARTICULATION_FLAG_FIXED_BASE) != 0);
        // The page states self collision as a permission, PhysX states it as a
        // prohibition, so the sense is inverted exactly once, here.
        articulation->setArticulationFlag(
            PxArticulationFlag::eDISABLE_SELF_COLLISION,
            (desc.flags & OPENUSD_PHYSX_ARTICULATION_FLAG_SELF_COLLISION) == 0);
        articulation->setSolverIterationCounts(
            desc.position_iterations > 0 ? desc.position_iterations : scene_desc.position_iterations,
            desc.velocity_iterations > 0 ? desc.velocity_iterations : scene_desc.velocity_iterations);
        // A sleep threshold of zero can never be reached from above, so it is
        // also how an articulation is asked never to sleep.
        if ((desc.flags & OPENUSD_PHYSX_ARTICULATION_FLAG_DISABLE_SLEEPING) != 0)
        {
            articulation->setSleepThreshold(0.0F);
        }
        else if (desc.sleep_threshold > 0.0F)
        {
            articulation->setSleepThreshold(desc.sleep_threshold);
        }
        if (desc.stabilization_threshold > 0.0F)
        {
            articulation->setStabilizationThreshold(desc.stabilization_threshold);
        }
        if (desc.wake_counter > 0.0F)
        {
            articulation->setWakeCounter(desc.wake_counter);
        }

        const size_t link_offset = static_cast<size_t>(desc.link_offset);
        const size_t link_count = static_cast<size_t>(desc.link_count);
        const size_t record_offset = world.articulation_links.size();
        // Local index within this window, so a parent lookup never leaves it.
        std::unordered_map<uint64_t, PxArticulationLink*> link_by_id;
        link_by_id.reserve(link_count);
        std::vector<PxArticulationLink*> links_by_slot;
        links_by_slot.reserve(link_count);

        for (size_t slot = 0; slot < link_count; ++slot)
        {
            const openusd_physx_articulation_link_desc link_desc =
                view.Get<openusd_physx_articulation_link_desc>(header.articulation_links, link_offset + slot);
            PxArticulationLink* parent = nullptr;
            if (slot != 0)
            {
                const auto found = link_by_id.find(link_desc.parent_id);
                if (found == link_by_id.end())
                {
                    return FailBuild(
                        world,
                        error,
                        "Articulation " + std::to_string(index) + " link " + std::to_string(slot) +
                            " names a parent that is not part of the articulation.",
                        OPENUSD_PHYSX_STATUS_INVALID_PAGE);
                }
                parent = found->second;
            }

            PxArticulationLink* link =
                articulation->createLink(parent, openusd_physx_translate::ToPx(link_desc.world_pose));
            if (link == nullptr)
            {
                return FailBuild(
                    world,
                    error,
                    "PhysX could not create articulation " + std::to_string(index) + " link " +
                        std::to_string(slot) + ".",
                    OPENUSD_PHYSX_STATUS_NATIVE_ERROR);
            }
            link_by_id.emplace(link_desc.id, link);
            links_by_slot.push_back(link);

            float first_density = 1000.0F;
            reason.clear();
            if (!attach_shapes(
                    *link,
                    link_desc.shape_offset,
                    link_desc.shape_count,
                    link_desc.collision_group,
                    // The suppressed pair matrix is indexed by actor, so a link
                    // owner index is pushed past the last actor and can never
                    // alias one.
                    header.actors.count + record_offset + slot,
                    desc.scene_index,
                    false,
                    first_density))
            {
                return FailBuild(world, error, reason, OPENUSD_PHYSX_STATUS_NATIVE_ERROR);
            }

            link->setLinearDamping(link_desc.linear_damping);
            link->setAngularDamping(link_desc.angular_damping);
            if (link_desc.max_linear_velocity > 0.0F)
            {
                link->setMaxLinearVelocity(link_desc.max_linear_velocity);
            }
            if (link_desc.max_angular_velocity > 0.0F)
            {
                link->setMaxAngularVelocity(link_desc.max_angular_velocity);
            }
            if ((link_desc.flags & OPENUSD_PHYSX_ARTICULATION_LINK_FLAG_DISABLE_GRAVITY) != 0)
            {
                link->setActorFlag(PxActorFlag::eDISABLE_GRAVITY, true);
            }
            if ((desc.flags & OPENUSD_PHYSX_ARTICULATION_FLAG_ENABLE_GYROSCOPIC_FORCES) != 0)
            {
                link->setRigidBodyFlag(PxRigidBodyFlag::eENABLE_GYROSCOPIC_FORCES, true);
            }

            // Mass resolution follows the rigid body path exactly so a link and
            // a body authored the same way weigh the same.
            const PxVec3 center_of_mass = openusd_physx_translate::ToPx(link_desc.center_of_mass);
            const PxVec3 inertia = openusd_physx_translate::ToPx(link_desc.inertia);
            const PxQuat principal_axes = openusd_physx_translate::ToPx(
                openusd_physx_support::ResolveRotationOrIdentity(link_desc.principal_axes));
            if (link_desc.mass > 0.0F)
            {
                if (inertia.x > 0.0F && inertia.y > 0.0F && inertia.z > 0.0F)
                {
                    link->setCMassLocalPose(PxTransform(center_of_mass, principal_axes));
                    link->setMass(link_desc.mass);
                    link->setMassSpaceInertiaTensor(inertia);
                }
                else
                {
                    PxRigidBodyExt::setMassAndUpdateInertia(*link, link_desc.mass, &center_of_mass);
                }
            }
            else if (link_desc.shape_count > 0)
            {
                PxRigidBodyExt::updateMassAndInertia(*link, first_density, &center_of_mass);
            }
            else
            {
                link->setCMassLocalPose(PxTransform(center_of_mass, principal_axes));
                link->setMass(1.0F);
                link->setMassSpaceInertiaTensor(PxVec3(1.0F, 1.0F, 1.0F));
            }

            PxArticulationJointReducedCoordinate* joint = link->getInboundJoint();
            if (joint != nullptr)
            {
                joint->setJointType(ToArticulationJointType(link_desc.joint_type));
                joint->setParentPose(openusd_physx_translate::ToPx(link_desc.parent_frame));
                joint->setChildPose(openusd_physx_translate::ToPx(link_desc.child_frame));
                if (link_desc.joint_friction > 0.0F)
                {
                    joint->setFrictionCoefficient(link_desc.joint_friction);
                }
                const float max_joint_velocity = link_desc.max_joint_velocity > 0.0F
                    ? link_desc.max_joint_velocity
                    : desc.max_joint_velocity;
                if (max_joint_velocity > 0.0F)
                {
                    joint->setMaxJointVelocity(max_joint_velocity);
                }
                const PxArticulationDriveType::Enum link_drive_type =
                    (link_desc.flags & OPENUSD_PHYSX_ARTICULATION_LINK_FLAG_DRIVE_ACCELERATION) != 0
                        ? PxArticulationDriveType::eACCELERATION
                        : PxArticulationDriveType::eFORCE;
                for (size_t axis = 0; axis < OPENUSD_PHYSX_JOINT_AXIS_COUNT; ++axis)
                {
                    const PxArticulationAxis::Enum px_axis = ToArticulationAxis(axis);
                    const uint32_t motion = link_desc.motion[axis];
                    joint->setMotion(px_axis, ToArticulationMotion(motion));
                    if (motion == OPENUSD_PHYSX_JOINT_MOTION_LIMITED)
                    {
                        joint->setLimitParams(
                            px_axis,
                            PxArticulationLimit(link_desc.lower_limit[axis], link_desc.upper_limit[axis]));
                    }
                    if (link_desc.armature[axis] > 0.0F)
                    {
                        joint->setArmature(px_axis, link_desc.armature[axis]);
                    }
                    if (motion == OPENUSD_PHYSX_JOINT_MOTION_LOCKED ||
                        (link_desc.drive_flags[axis] & OPENUSD_PHYSX_JOINT_DRIVE_FLAG_ENABLED) == 0)
                    {
                        continue;
                    }
                    const PxArticulationDriveType::Enum drive_type =
                        (link_desc.drive_flags[axis] & OPENUSD_PHYSX_JOINT_DRIVE_FLAG_ACCELERATION) != 0
                            ? PxArticulationDriveType::eACCELERATION
                            : link_drive_type;
                    joint->setDriveParams(
                        px_axis,
                        PxArticulationDrive(
                            link_desc.drive_stiffness[axis],
                            link_desc.drive_damping[axis],
                            link_desc.drive_max_force[axis] > 0.0F ? link_desc.drive_max_force[axis] : PX_MAX_F32,
                            drive_type));
                    joint->setDriveTarget(px_axis, link_desc.drive_target_position[axis], false);
                    joint->setDriveVelocity(px_axis, link_desc.drive_target_velocity[axis], false);
                }
            }

            link->userData = MakeActorUserData(kUserDataKindLink, record_offset + slot);

            WorldArticulationLink link_record;
            link_record.id = link_desc.id;
            link_record.link = link;
            // The articulation this link belongs to is pushed below, once every
            // link exists, so the index it is about to take is the current size.
            // A build that fails between here and there discards the whole world
            // rather than retaining it, so the index can never dangle.
            link_record.articulation_index = static_cast<uint32_t>(world.articulations.size());
            link_record.initial.id = link_desc.id;
            link_record.initial.pose = link_desc.world_pose;
            link_record.initial.flags = OPENUSD_PHYSX_BODY_STATE_FLAG_ARTICULATION_LINK;
            world.articulation_link_by_id.emplace(link_desc.id, world.articulation_links.size());
            world.articulation_links.push_back(link_record);
        }

        // Tendons and mimic joints couple axes that the tree topology cannot,
        // and PhysX only accepts them while the articulation is out of a scene,
        // so they are created here, after every link exists and before the
        // articulation is added below.
        for (size_t tendon_index = 0; tendon_index < header.articulation_tendons.count; ++tendon_index)
        {
            const openusd_physx_tendon_desc tendon =
                view.Get<openusd_physx_tendon_desc>(header.articulation_tendons, tendon_index);
            if (static_cast<size_t>(tendon.articulation_index) != index)
            {
                continue;
            }
            const size_t node_offset = static_cast<size_t>(tendon.node_offset);
            const size_t node_count = static_cast<size_t>(tendon.node_count);
            const bool limited = (tendon.flags & OPENUSD_PHYSX_TENDON_FLAG_LIMIT_ENABLED) != 0;

            // PhysX only accepts a rest length and a limit on a leaf, so which
            // nodes are leaves is decided from the page before anything is
            // created rather than discovered halfway through.
            std::vector<bool> has_child(node_count, false);
            for (size_t node_slot = 0; node_slot < node_count; ++node_slot)
            {
                const openusd_physx_tendon_node_desc node =
                    view.Get<openusd_physx_tendon_node_desc>(header.articulation_tendon_nodes, node_offset + node_slot);
                if (node.parent_index != 0)
                {
                    has_child[static_cast<size_t>(node.parent_index) - 1U] = true;
                }
            }

            if (tendon.type == OPENUSD_PHYSX_TENDON_SPATIAL)
            {
                PxArticulationSpatialTendon* spatial = articulation->createSpatialTendon();
                if (spatial == nullptr)
                {
                    return FailBuild(
                        world,
                        error,
                        "PhysX could not create spatial tendon " + std::to_string(tendon_index) + ".",
                        OPENUSD_PHYSX_STATUS_NATIVE_ERROR);
                }
                spatial->setStiffness(tendon.stiffness);
                spatial->setDamping(tendon.damping);
                spatial->setLimitStiffness(tendon.limit_stiffness);
                spatial->setOffset(tendon.offset, false);

                std::vector<PxArticulationAttachment*> attachments(node_count, nullptr);
                for (size_t node_slot = 0; node_slot < node_count; ++node_slot)
                {
                    const openusd_physx_tendon_node_desc node =
                        view.Get<openusd_physx_tendon_node_desc>(header.articulation_tendon_nodes, node_offset + node_slot);
                    PxArticulationAttachment* parent =
                        node.parent_index != 0 ? attachments[static_cast<size_t>(node.parent_index) - 1U] : nullptr;
                    PxArticulationAttachment* attachment = spatial->createAttachment(
                        parent,
                        node.coefficient,
                        openusd_physx_translate::ToPx(node.relative_offset),
                        links_by_slot[static_cast<size_t>(node.link_index)]);
                    if (attachment == nullptr)
                    {
                        return FailBuild(
                            world,
                            error,
                            "PhysX could not create spatial tendon attachment " + std::to_string(node_slot) +
                                " of tendon " + std::to_string(tendon_index) + ".",
                            OPENUSD_PHYSX_STATUS_NATIVE_ERROR);
                    }
                    attachments[node_slot] = attachment;
                    if (!has_child[node_slot])
                    {
                        attachment->setRestLength(node.rest_length);
                        if (limited)
                        {
                            PxArticulationTendonLimit limit;
                            limit.lowLimit = node.low_limit;
                            limit.highLimit = node.high_limit;
                            attachment->setLimitParameters(limit);
                        }
                    }
                }
            }
            else
            {
                PxArticulationFixedTendon* fixed = articulation->createFixedTendon();
                if (fixed == nullptr)
                {
                    return FailBuild(
                        world,
                        error,
                        "PhysX could not create fixed tendon " + std::to_string(tendon_index) + ".",
                        OPENUSD_PHYSX_STATUS_NATIVE_ERROR);
                }
                fixed->setStiffness(tendon.stiffness);
                fixed->setDamping(tendon.damping);
                fixed->setLimitStiffness(tendon.limit_stiffness);
                fixed->setOffset(tendon.offset, false);
                fixed->setRestLength(tendon.rest_length);
                if (limited)
                {
                    PxArticulationTendonLimit limit;
                    limit.lowLimit = tendon.low_limit;
                    limit.highLimit = tendon.high_limit;
                    fixed->setLimitParameters(limit);
                }

                std::vector<PxArticulationTendonJoint*> tendon_joints(node_count, nullptr);
                for (size_t node_slot = 0; node_slot < node_count; ++node_slot)
                {
                    const openusd_physx_tendon_node_desc node =
                        view.Get<openusd_physx_tendon_node_desc>(header.articulation_tendon_nodes, node_offset + node_slot);
                    // The simulation SDK only accepts a tendon joint on an axis
                    // that actually moves, and rejecting a locked axis or a fixed
                    // inbound joint here turns what would otherwise be a silent
                    // null tendon joint into a named page error. The root node
                    // only anchors the tendon, so it is allowed to name the
                    // articulation root, which has no inbound joint at all.
                    const openusd_physx_articulation_link_desc node_link =
                        view.Get<openusd_physx_articulation_link_desc>(
                            header.articulation_links, link_offset + static_cast<size_t>(node.link_index));
                    if (node.parent_index != 0 &&
                        (node_link.joint_type == OPENUSD_PHYSX_ARTICULATION_JOINT_FIXED ||
                            node_link.joint_type == OPENUSD_PHYSX_ARTICULATION_JOINT_NONE ||
                            node_link.motion[node.axis] == OPENUSD_PHYSX_JOINT_MOTION_LOCKED))
                    {
                        return FailBuild(
                            world,
                            error,
                            "Fixed tendon " + std::to_string(tendon_index) + " node " + std::to_string(node_slot) +
                                " drives an axis that its link cannot move.",
                            OPENUSD_PHYSX_STATUS_INVALID_PAGE);
                    }
                    PxArticulationTendonJoint* parent =
                        node.parent_index != 0 ? tendon_joints[static_cast<size_t>(node.parent_index) - 1U] : nullptr;
                    PxArticulationTendonJoint* tendon_joint = fixed->createTendonJoint(
                        parent,
                        ToArticulationAxis(node.axis),
                        node.coefficient,
                        node.recip_coefficient,
                        links_by_slot[static_cast<size_t>(node.link_index)]);
                    if (tendon_joint == nullptr)
                    {
                        return FailBuild(
                            world,
                            error,
                            "PhysX could not create fixed tendon joint " + std::to_string(node_slot) +
                                " of tendon " + std::to_string(tendon_index) + ".",
                            OPENUSD_PHYSX_STATUS_NATIVE_ERROR);
                    }
                    tendon_joints[node_slot] = tendon_joint;
                }
            }
            ++world.tendon_count;
        }

        for (size_t mimic_index = 0; mimic_index < header.articulation_mimic_joints.count; ++mimic_index)
        {
            const openusd_physx_mimic_joint_desc mimic =
                view.Get<openusd_physx_mimic_joint_desc>(header.articulation_mimic_joints, mimic_index);
            if (static_cast<size_t>(mimic.articulation_index) != index)
            {
                continue;
            }
            PxArticulationJointReducedCoordinate* joint_a =
                links_by_slot[static_cast<size_t>(mimic.link_a)]->getInboundJoint();
            PxArticulationJointReducedCoordinate* joint_b =
                links_by_slot[static_cast<size_t>(mimic.link_b)]->getInboundJoint();
            if (joint_a == nullptr || joint_b == nullptr)
            {
                return FailBuild(
                    world,
                    error,
                    "Mimic joint " + std::to_string(mimic_index) + " names a link without an inbound joint.",
                    OPENUSD_PHYSX_STATUS_INVALID_PAGE);
            }
            PxArticulationMimicJoint* mimic_joint = articulation->createMimicJoint(
                *joint_a,
                ToArticulationAxis(mimic.axis_a),
                *joint_b,
                ToArticulationAxis(mimic.axis_b),
                mimic.gear_ratio,
                mimic.offset,
                mimic.natural_frequency,
                mimic.damping_ratio);
            if (mimic_joint == nullptr)
            {
                return FailBuild(
                    world,
                    error,
                    "PhysX could not create mimic joint " + std::to_string(mimic_index) + ".",
                    OPENUSD_PHYSX_STATUS_NATIVE_ERROR);
            }
            ++world.mimic_joint_count;
        }

        world.scenes[static_cast<size_t>(desc.scene_index)]->addArticulation(*articulation);

        WorldArticulation record;
        record.id = desc.id;
        record.articulation = articulation_scope.Detach();
        record.scene_index = static_cast<uint32_t>(desc.scene_index);
        record.link_offset = record_offset;
        record.link_count = link_count;
        world.articulations.push_back(record);
    }

    // -----------------------------------------------------------------------
    // Character controllers. One manager per scene, created only for the scenes
    // that actually own a controller, and released before the scene is.
    // -----------------------------------------------------------------------
    if (header.controllers.count > 0)
    {
        world.controller_managers.assign(world.scenes.size(), nullptr);
        world.controllers.reserve(header.controllers.count);
        for (size_t index = 0; index < header.controllers.count; ++index)
        {
            const openusd_physx_controller_desc desc =
                view.Get<openusd_physx_controller_desc>(header.controllers, index);
            const size_t scene_slot = static_cast<size_t>(desc.scene_index);
            if (world.controller_managers[scene_slot] == nullptr)
            {
                world.controller_managers[scene_slot] = PxCreateControllerManager(*world.scenes[scene_slot]);
                if (world.controller_managers[scene_slot] == nullptr)
                {
                    return FailBuild(
                        world,
                        error,
                        "PhysX could not create a controller manager for scene " + std::to_string(scene_slot) + ".",
                        OPENUSD_PHYSX_STATUS_NATIVE_ERROR);
                }
            }

            // An all zero up direction is the page saying "use the stage up
            // axis", which is the only direction a controller can default to.
            PxVec3 up = openusd_physx_translate::ToPx(desc.up_direction);
            if (!(up.magnitudeSquared() > 0.0F))
            {
                up = UpAxisVector(header.up_axis);
            }
            up.normalize();

            PxMaterial* material = ResolveMaterial(world, desc.material_index);
            PxController* controller = nullptr;
            if (desc.shape == OPENUSD_PHYSX_CONTROLLER_BOX)
            {
                PxBoxControllerDesc box;
                box.halfHeight = desc.half_extents.y;
                box.halfSideExtent = desc.half_extents.x;
                box.halfForwardExtent = desc.half_extents.z;
                FillControllerDesc(box, desc, up, material, world);
                controller = box.isValid()
                    ? world.controller_managers[scene_slot]->createController(box)
                    : nullptr;
            }
            else
            {
                PxCapsuleControllerDesc capsule;
                capsule.radius = desc.radius;
                capsule.height = desc.height;
                capsule.climbingMode = desc.climbing_mode == OPENUSD_PHYSX_CONTROLLER_CLIMBING_CONSTRAINED
                    ? PxCapsuleClimbingMode::eCONSTRAINED
                    : PxCapsuleClimbingMode::eEASY;
                FillControllerDesc(capsule, desc, up, material, world);
                controller = capsule.isValid()
                    ? world.controller_managers[scene_slot]->createController(capsule)
                    : nullptr;
            }
            if (controller == nullptr)
            {
                return FailBuild(
                    world,
                    error,
                    "PhysX could not create character controller " + std::to_string(index) + ".",
                    OPENUSD_PHYSX_STATUS_NATIVE_ERROR);
            }

            // The controller owns a kinematic actor. Tagging it keeps contact
            // and query reports able to name the controller prim, and the
            // filter data keeps the controller inside its collision group.
            PxRigidDynamic* controller_actor = controller->getActor();
            if (controller_actor != nullptr)
            {
                controller_actor->userData = MakeActorUserData(kUserDataKindController, index);
                PxShape* controller_shape = nullptr;
                if (controller_actor->getNbShapes() == 1)
                {
                    controller_actor->getShapes(&controller_shape, 1);
                }
                if (controller_shape != nullptr)
                {
                    PxFilterData filter_data;
                    filter_data.word0 = 1U << (desc.collision_group % OPENUSD_PHYSX_MAX_COLLISION_GROUPS);
                    filter_data.word1 = 0xFFFFFFFFU;
                    filter_data.word2 = static_cast<PxU32>(
                        header.actors.count + header.articulation_links.count + index);
                    filter_data.word3 = 0U;
                    controller_shape->setSimulationFilterData(filter_data);
                    controller_shape->setQueryFilterData(filter_data);
                }
            }
            controller->setUserData(reinterpret_cast<void*>(static_cast<uintptr_t>(index + 1)));

            WorldController record;
            record.id = desc.id;
            record.controller = controller;
            record.scene_index = static_cast<uint32_t>(desc.scene_index);
            record.flags = desc.flags;
            record.up = up;
            record.initial.id = desc.id;
            record.initial.pose.position = desc.position;
            record.initial.pose.rotation = openusd_physx_quatf{0.0F, 0.0F, 0.0F, 1.0F};
            record.initial.flags =
                OPENUSD_PHYSX_BODY_STATE_FLAG_CONTROLLER | OPENUSD_PHYSX_BODY_STATE_FLAG_KINEMATIC;
            world.controller_by_id.emplace(record.id, world.controllers.size());
            world.controllers.push_back(record);
        }
    }

    // -----------------------------------------------------------------------
    // Vehicles. The chassis is an actor the actor section already built, so a
    // vehicle only adds the drivetrain, suspension and tire state PhysX keeps
    // outside the rigid body, plus the custom suspension limit constraints.
    // -----------------------------------------------------------------------
    if (header.vehicles.count > 0)
    {
        std::string vehicle_reason;
        if (!openusd_physx_vehicle::Initialize(openusd_physx_runtime::Foundation(), vehicle_reason))
        {
            return FailBuild(world, error, vehicle_reason, OPENUSD_PHYSX_STATUS_NATIVE_ERROR);
        }
        world.vehicles.reserve(header.vehicles.count);
        world.vehicle_by_id.reserve(header.vehicles.count);
        for (size_t index = 0; index < header.vehicles.count; ++index)
        {
            const openusd_physx_vehicle_desc desc =
                view.Get<openusd_physx_vehicle_desc>(header.vehicles, index);
            const size_t actor_slot = static_cast<size_t>(desc.actor_index);
            PxRigidBody* chassis = static_cast<PxRigidDynamic*>(world.actors[actor_slot].actor);

            // A sweep query needs a cooked unit cylinder. It is cooked once per
            // world, and only when a vehicle actually asks for a sweep.
            if (desc.query == OPENUSD_PHYSX_VEHICLE_QUERY_SWEEP && world.vehicle_sweep_mesh == nullptr)
            {
                world.vehicle_sweep_mesh = openusd_physx_vehicle::CreateSweepMesh(
                    physics, desc.longitudinal_axis, desc.lateral_axis, desc.vertical_axis);
                if (world.vehicle_sweep_mesh == nullptr)
                {
                    PushDiagnostic(
                        world,
                        OPENUSD_PHYSX_DIAGNOSTIC_WARNING,
                        OPENUSD_PHYSX_DIAGNOSTIC_COOKING_FAILED,
                        desc.id,
                        "A vehicle asked for a sweep road query, but the simulation SDK could not cook the sweep shape, so a raycast query is used instead.");
                }
            }

            WorldVehicle record;
            record.id = desc.id;
            record.scene_index = static_cast<uint32_t>(desc.scene_index);
            record.actor_index = actor_slot;
            record.instance = std::make_unique<openusd_physx_vehicle::Instance>();
            // Copied into one contiguous block so the vehicle module never has
            // to know how a page window is addressed.
            std::vector<openusd_physx_vehicle_wheel_desc> wheels(static_cast<size_t>(desc.wheel_count));
            for (size_t wheel_slot = 0; wheel_slot < wheels.size(); ++wheel_slot)
            {
                wheels[wheel_slot] = view.Get<openusd_physx_vehicle_wheel_desc>(
                    header.vehicle_wheels, static_cast<size_t>(desc.wheel_offset) + wheel_slot);
            }
            vehicle_reason.clear();
            if (!record.instance->Configure(
                    desc,
                    wheels.data(),
                    *chassis,
                    *world.scenes[static_cast<size_t>(desc.scene_index)],
                    physics,
                    world.default_material,
                    world.vehicle_sweep_mesh,
                    vehicle_reason))
            {
                return FailBuild(world, error, vehicle_reason, OPENUSD_PHYSX_STATUS_NATIVE_ERROR);
            }
            world.vehicle_wheel_count += record.instance->WheelCount();
            if (record.instance->PublishesWheels())
            {
                world.published_vehicle_wheel_count += record.instance->WheelCount();
            }
            // The gearbox already sits on a start gear chosen by the autobox
            // flag, so the record must agree with it before it is published.
            // Leaving the default zero here would make the very first step
            // report a gear change that no command and no autobox caused.
            record.last_gear = record.instance->CurrentGear();
            world.vehicle_by_id.emplace(record.id, world.vehicles.size());
            world.vehicles.push_back(std::move(record));
        }
    }

    // The CUDA accelerated domains are built last, after every CPU object of the
    // same page exists, so a particle system or a deformable that cannot be
    // created costs exactly itself: the rigid bodies, joints, articulations,
    // controllers, and vehicles of the same build are already complete and keep
    // simulating. Each skipped object is reported by identity.
    {
        std::vector<openusd_physx_gpu::SkipNote> skipped;
        openusd_physx_gpu::Build(view, world.scenes, world.scene_is_gpu, world.gpu, skipped);
        for (const openusd_physx_gpu::SkipNote& note : skipped)
        {
            PushDiagnostic(
                world,
                OPENUSD_PHYSX_DIAGNOSTIC_WARNING,
                note.id == OPENUSD_PHYSX_INVALID_ID ? OPENUSD_PHYSX_DIAGNOSTIC_GPU_UNAVAILABLE
                                                    : OPENUSD_PHYSX_DIAGNOSTIC_GPU_OBJECT_SKIPPED,
                note.id,
                note.message);
        }
    }

    // Reserving the declared capacity up front is what keeps every later step
    // allocation free: the sink never grows and never shrinks after this point.
    // The published body count is what a result page must size for, and it
    // covers movable actors, articulation links, and controllers alike.
    world.dynamic_actor_count += static_cast<uint32_t>(world.articulation_links.size());
    world.dynamic_actor_count += static_cast<uint32_t>(world.controllers.size());
    world.dynamic_actor_count += world.published_vehicle_wheel_count;
    world.event_sink.Reserve(world.capacities.max_events);
    world.diagnostics.reserve(std::min<size_t>(world.capacities.max_diagnostics, 256));
    world.debug_lines.reserve(std::min<size_t>(world.capacities.max_debug_lines, 1024));
    world.state = OPENUSD_PHYSX_WORLD_STATE_READY;
    return OPENUSD_PHYSX_STATUS_OK;
}

// Applies one command to a reduced coordinate articulation link.
//
// A link is a PxRigidBody, so a force, a torque and an acceleration apply to it
// exactly as they do to a dynamic actor. Three families do not:
//
//  - PhysX documents that PxForceMode::eIMPULSE and eVELOCITY_CHANGE "can not be
//    applied to articulation links", on addForce, addTorque, clearForce and
//    clearTorque alike. Every impulse command is therefore refused rather than
//    quietly handed to the SDK, and a clear only clears the force accumulator,
//    which is the only one a link has.
//  - Velocity and pose cannot be stated directly: a link's linear and angular
//    velocity are functions of the joint degrees of freedom above it, and PhysX
//    declares setLinearVelocity, setAngularVelocity, setKinematicTarget and
//    setGlobalPose on PxRigidDynamic rather than on PxRigidBody for that reason.
//  - Sleeping is a property of the whole articulation rather than of one link, so
//    wake and sleep route to the articulation the link belongs to.
//
// Everything refused here reports a diagnostic that says why. Silently dropping
// an interaction would leave an operator dragging a control that does nothing.
void ApplyArticulationLinkCommand(
    openusd_physx_world& world,
    const openusd_physx_command& command,
    WorldArticulationLink& record)
{
    const auto reject = [&](const char* message)
    {
        PushDiagnostic(
            world,
            OPENUSD_PHYSX_DIAGNOSTIC_WARNING,
            OPENUSD_PHYSX_DIAGNOSTIC_COMMAND_REJECTED,
            record.id,
            message);
    };

    PxArticulationLink* link = record.link;
    if (link == nullptr)
    {
        reject("An articulation link command targets a link this world no longer holds.");
        return;
    }

    const PxVec3 vector =
        openusd_physx_translate::ToPx(openusd_physx_events::ResolveCommandVector(command));
    const bool wake = (command.flags & OPENUSD_PHYSX_COMMAND_FLAG_NO_WAKE) == 0;
    const bool local_point = (command.flags & OPENUSD_PHYSX_COMMAND_FLAG_POINT_LOCAL) != 0;
    const bool center_of_mass =
        (command.flags & OPENUSD_PHYSX_COMMAND_FLAG_POINT_CENTER_OF_MASS) != 0;

    const auto force_mode = [&](PxForceMode::Enum base)
    {
        if ((command.flags & OPENUSD_PHYSX_COMMAND_FLAG_MODE_ACCELERATION) != 0)
        {
            return PxForceMode::eACCELERATION;
        }
        if ((command.flags & OPENUSD_PHYSX_COMMAND_FLAG_MODE_VELOCITY_CHANGE) != 0)
        {
            return PxForceMode::eVELOCITY_CHANGE;
        }
        return base;
    };

    const auto apply_at_point = [&](PxForceMode::Enum mode)
    {
        if (center_of_mass)
        {
            link->addForce(vector, mode, wake);
            return;
        }
        const PxVec3 point = openusd_physx_translate::ToPx(command.point);
        if (local_point)
        {
            PxRigidBodyExt::addForceAtLocalPos(*link, vector, point, mode, wake);
            return;
        }
        PxRigidBodyExt::addForceAtPos(*link, vector, point, mode, wake);
    };

    const auto owner = [&]() -> PxArticulationReducedCoordinate*
    {
        const size_t index = static_cast<size_t>(record.articulation_index);
        return index < world.articulations.size()
            ? world.articulations[index].articulation
            : nullptr;
    };

    /* The force mode modifiers reach this switch already filtered. The pinned
     * SDK states on PxRigidBody::addForce that PxForceMode::eIMPULSE and
     * PxForceMode::eVELOCITY_CHANGE cannot be applied to an articulation link,
     * and AllowedCommandFlags only offers MODE_VELOCITY_CHANGE on the impulse
     * commands, which this switch refuses by name below. A force or a torque
     * can therefore only carry MODE_ACCELERATION, which a link does accept, so
     * force_mode never yields a mode the link would reject. */
    switch (command.type)
    {
    case OPENUSD_PHYSX_COMMAND_ADD_FORCE:
        link->addForce(vector, force_mode(PxForceMode::eFORCE), wake);
        return;
    case OPENUSD_PHYSX_COMMAND_ADD_TORQUE:
        link->addTorque(vector, force_mode(PxForceMode::eFORCE), wake);
        return;
    case OPENUSD_PHYSX_COMMAND_ADD_IMPULSE:
        reject(
            "An articulation link cannot take an impulse: PhysX does not accept the impulse or "
            "velocity change force modes on a link. Apply a force over the step instead.");
        return;
    case OPENUSD_PHYSX_COMMAND_ADD_ANGULAR_IMPULSE:
        reject(
            "An articulation link cannot take an angular impulse: PhysX does not accept the "
            "impulse or velocity change force modes on a link. Apply a torque instead.");
        return;
    case OPENUSD_PHYSX_COMMAND_ADD_FORCE_AT_POINT:
        apply_at_point(PxForceMode::eFORCE);
        return;
    case OPENUSD_PHYSX_COMMAND_ADD_IMPULSE_AT_POINT:
        reject(
            "An articulation link cannot take an impulse at a point: PhysX does not accept the "
            "impulse force mode on a link. Apply a force at that point instead.");
        return;
    case OPENUSD_PHYSX_COMMAND_CLEAR_FORCE:
        // A link accumulates only the force/acceleration pair; the impulse and
        // velocity change accumulators PhysX clears for a dynamic actor do not
        // exist on a link, and asking for them is rejected by the SDK.
        link->clearForce(PxForceMode::eFORCE);
        return;
    case OPENUSD_PHYSX_COMMAND_CLEAR_TORQUE:
        link->clearTorque(PxForceMode::eFORCE);
        return;
    case OPENUSD_PHYSX_COMMAND_WAKE:
        if (PxArticulationReducedCoordinate* articulation = owner())
        {
            articulation->wakeUp();
            return;
        }
        reject("A wake command targets a link whose articulation this world no longer holds.");
        return;
    case OPENUSD_PHYSX_COMMAND_SLEEP:
        if (PxArticulationReducedCoordinate* articulation = owner())
        {
            articulation->putToSleep();
            return;
        }
        reject("A sleep command targets a link whose articulation this world no longer holds.");
        return;
    case OPENUSD_PHYSX_COMMAND_SET_LINEAR_VELOCITY:
        reject(
            "An articulation link cannot be given a linear velocity directly: its velocity is a "
            "function of the joint degrees of freedom above it. Drive the joint instead.");
        return;
    case OPENUSD_PHYSX_COMMAND_SET_ANGULAR_VELOCITY:
        reject(
            "An articulation link cannot be given an angular velocity directly: its velocity is a "
            "function of the joint degrees of freedom above it. Drive the joint instead.");
        return;
    case OPENUSD_PHYSX_COMMAND_KINEMATIC_TARGET:
        reject("An articulation link cannot be driven kinematically.");
        return;
    case OPENUSD_PHYSX_COMMAND_TELEPORT:
        reject(
            "An articulation link cannot be teleported: moving one link alone would contradict the "
            "reduced coordinate state of its articulation.");
        return;
    default:
        reject("The command type is not supported by an articulation link.");
        return;
    }
}

void ApplyCommand(openusd_physx_world& world, const openusd_physx_command& command)
{
    if (command.type == OPENUSD_PHYSX_COMMAND_SET_SCENE_GRAVITY)
    {
        const auto scene = world.scene_by_id.find(command.target_id);
        if (scene == world.scene_by_id.end())
        {
            PushDiagnostic(
                world,
                OPENUSD_PHYSX_DIAGNOSTIC_WARNING,
                OPENUSD_PHYSX_DIAGNOSTIC_COMMAND_TARGET_MISSING,
                command.target_id,
                "A gravity command targets a scene identity that this world does not contain.");
            return;
        }
        world.scenes[scene->second]->setGravity(
            openusd_physx_translate::ToPx(openusd_physx_events::ResolveCommandVector(command)));
        return;
    }

    if (command.type == OPENUSD_PHYSX_COMMAND_MOVE_CONTROLLER)
    {
        const auto controller = world.controller_by_id.find(command.target_id);
        if (controller == world.controller_by_id.end())
        {
            PushDiagnostic(
                world,
                OPENUSD_PHYSX_DIAGNOSTIC_WARNING,
                OPENUSD_PHYSX_DIAGNOSTIC_COMMAND_TARGET_MISSING,
                command.target_id,
                "A move command targets a controller identity that this world does not contain.");
            return;
        }
        // Displacements accumulate so that several commands in one batch move
        // the controller once, which is what keeps the sweep count bounded.
        WorldController& record = world.controllers[controller->second];
        record.pending_move +=
            openusd_physx_translate::ToPx(openusd_physx_events::ResolveCommandVector(command));
        record.has_pending_move = true;
        return;
    }

    if (command.type == OPENUSD_PHYSX_COMMAND_VEHICLE_INPUT)
    {
        const auto vehicle = world.vehicle_by_id.find(command.target_id);
        if (vehicle == world.vehicle_by_id.end())
        {
            PushDiagnostic(
                world,
                OPENUSD_PHYSX_DIAGNOSTIC_WARNING,
                OPENUSD_PHYSX_DIAGNOSTIC_COMMAND_TARGET_MISSING,
                command.target_id,
                "A vehicle input command targets a vehicle identity that this world does not contain.");
            return;
        }
        // Driver input is a level, not an impulse, so the last command in a
        // batch wins rather than accumulating.
        WorldVehicle& record = world.vehicles[vehicle->second];
        record.throttle = command.vector.x;
        record.brake = command.vector.y;
        record.steer = command.vector.z;
        record.hand_brake = command.point.x;
        record.clutch = command.point.y;
        // The command validator already rejects a gear that is negative, non
        // integral, or beyond the gear budget, so the narrowing below can only
        // see a value the fixed gearbox arrays accept. Clamping keeps that true
        // even if the validator is ever relaxed.
        const float gear = std::floor(std::isfinite(command.point.z) ? command.point.z : 0.0F);
        const float bounded_gear =
            std::min(std::max(gear, 0.0F), static_cast<float>(OPENUSD_PHYSX_MAX_VEHICLE_GEARS));
        record.gear = static_cast<uint32_t>(bounded_gear);
        return;
    }

    const auto entry = world.actor_by_id.find(command.target_id);
    if (entry == world.actor_by_id.end())
    {
        // A reduced coordinate link is addressed by its own prim identity but is
        // composed into its articulation rather than into the actor table, so it
        // is resolved here rather than being reported as a missing target.
        const auto link = world.articulation_link_by_id.find(command.target_id);
        if (link != world.articulation_link_by_id.end() &&
            link->second < world.articulation_links.size())
        {
            ApplyArticulationLinkCommand(
                world, command, world.articulation_links[link->second]);
            return;
        }

        if (world.controller_by_id.find(command.target_id) != world.controller_by_id.end())
        {
            PushDiagnostic(
                world,
                OPENUSD_PHYSX_DIAGNOSTIC_WARNING,
                OPENUSD_PHYSX_DIAGNOSTIC_COMMAND_REJECTED,
                command.target_id,
                "A character controller only accepts the move controller command.");
            return;
        }
        PushDiagnostic(
            world,
            OPENUSD_PHYSX_DIAGNOSTIC_WARNING,
            OPENUSD_PHYSX_DIAGNOSTIC_COMMAND_TARGET_MISSING,
            command.target_id,
            "A command targets an actor identity that this world does not contain.");
        return;
    }

    WorldActor& record = world.actors[entry->second];
    const bool kinematic = record.type == OPENUSD_PHYSX_ACTOR_KINEMATIC;
    const bool dynamic = record.type == OPENUSD_PHYSX_ACTOR_DYNAMIC;
    PxRigidDynamic* body = IsMovable(record.type) ? static_cast<PxRigidDynamic*>(record.actor) : nullptr;

    const auto reject = [&](const char* message)
    {
        PushDiagnostic(
            world,
            OPENUSD_PHYSX_DIAGNOSTIC_WARNING,
            OPENUSD_PHYSX_DIAGNOSTIC_COMMAND_REJECTED,
            record.id,
            message);
    };

    // The magnitude modifier is resolved once, so every branch below sees the
    // effective world vector and never re-reads the raw direction and scalar.
    const PxVec3 vector =
        openusd_physx_translate::ToPx(openusd_physx_events::ResolveCommandVector(command));
    const bool wake = (command.flags & OPENUSD_PHYSX_COMMAND_FLAG_NO_WAKE) == 0;
    const bool local_point = (command.flags & OPENUSD_PHYSX_COMMAND_FLAG_POINT_LOCAL) != 0;
    const bool center_of_mass =
        (command.flags & OPENUSD_PHYSX_COMMAND_FLAG_POINT_CENTER_OF_MASS) != 0;

    const auto force_mode = [&](PxForceMode::Enum base)
    {
        if ((command.flags & OPENUSD_PHYSX_COMMAND_FLAG_MODE_ACCELERATION) != 0)
        {
            return PxForceMode::eACCELERATION;
        }
        if ((command.flags & OPENUSD_PHYSX_COMMAND_FLAG_MODE_VELOCITY_CHANGE) != 0)
        {
            return PxForceMode::eVELOCITY_CHANGE;
        }
        return base;
    };

    // Applies a force or an impulse at the requested application point. A point
    // at the centre of mass is the same as a plain body force, so it never goes
    // through the extension helper.
    const auto apply_at_point = [&](PxForceMode::Enum mode)
    {
        if (center_of_mass)
        {
            body->addForce(vector, mode, wake);
            return;
        }
        const PxVec3 point = openusd_physx_translate::ToPx(command.point);
        if (local_point)
        {
            PxRigidBodyExt::addForceAtLocalPos(*body, vector, point, mode, wake);
            return;
        }
        PxRigidBodyExt::addForceAtPos(*body, vector, point, mode, wake);
    };

    switch (command.type)
    {
    case OPENUSD_PHYSX_COMMAND_KINEMATIC_TARGET:
        if (!kinematic)
        {
            reject("A kinematic target command was sent to an actor that is not kinematic.");
            return;
        }
        body->setKinematicTarget(openusd_physx_translate::ToPx(command.pose));
        return;
    case OPENUSD_PHYSX_COMMAND_TELEPORT:
        if (body == nullptr)
        {
            reject("A teleport command was sent to a static actor.");
            return;
        }
        body->setGlobalPose(openusd_physx_translate::ToPx(command.pose), wake);
        return;
    case OPENUSD_PHYSX_COMMAND_SET_LINEAR_VELOCITY:
        if (!dynamic)
        {
            reject("A linear velocity command was sent to an actor that is not dynamic.");
            return;
        }
        body->setLinearVelocity(vector, wake);
        return;
    case OPENUSD_PHYSX_COMMAND_SET_ANGULAR_VELOCITY:
        if (!dynamic)
        {
            reject("An angular velocity command was sent to an actor that is not dynamic.");
            return;
        }
        body->setAngularVelocity(vector, wake);
        return;
    case OPENUSD_PHYSX_COMMAND_ADD_FORCE:
        if (!dynamic)
        {
            reject("A force command was sent to an actor that is not dynamic.");
            return;
        }
        body->addForce(vector, force_mode(PxForceMode::eFORCE), wake);
        return;
    case OPENUSD_PHYSX_COMMAND_ADD_TORQUE:
        if (!dynamic)
        {
            reject("A torque command was sent to an actor that is not dynamic.");
            return;
        }
        body->addTorque(vector, force_mode(PxForceMode::eFORCE), wake);
        return;
    case OPENUSD_PHYSX_COMMAND_ADD_IMPULSE:
        if (!dynamic)
        {
            reject("An impulse command was sent to an actor that is not dynamic.");
            return;
        }
        body->addForce(vector, force_mode(PxForceMode::eIMPULSE), wake);
        return;
    case OPENUSD_PHYSX_COMMAND_ADD_ANGULAR_IMPULSE:
        if (!dynamic)
        {
            reject("An angular impulse command was sent to an actor that is not dynamic.");
            return;
        }
        body->addTorque(vector, force_mode(PxForceMode::eIMPULSE), wake);
        return;
    case OPENUSD_PHYSX_COMMAND_ADD_FORCE_AT_POINT:
        if (!dynamic)
        {
            reject("A force at point command was sent to an actor that is not dynamic.");
            return;
        }
        apply_at_point(force_mode(PxForceMode::eFORCE));
        return;
    case OPENUSD_PHYSX_COMMAND_ADD_IMPULSE_AT_POINT:
        if (!dynamic)
        {
            reject("An impulse at point command was sent to an actor that is not dynamic.");
            return;
        }
        apply_at_point(force_mode(PxForceMode::eIMPULSE));
        return;
    case OPENUSD_PHYSX_COMMAND_CLEAR_FORCE:
        if (!dynamic)
        {
            reject("A clear force command was sent to an actor that is not dynamic.");
            return;
        }
        // Clearing only affects the accumulator of the pending step, so a clear
        // that a batch places after an add always wins and a clear placed
        // before an add is overwritten by that add.
        body->clearForce(PxForceMode::eFORCE);
        body->clearForce(PxForceMode::eIMPULSE);
        return;
    case OPENUSD_PHYSX_COMMAND_CLEAR_TORQUE:
        if (!dynamic)
        {
            reject("A clear torque command was sent to an actor that is not dynamic.");
            return;
        }
        body->clearTorque(PxForceMode::eFORCE);
        body->clearTorque(PxForceMode::eIMPULSE);
        return;
    case OPENUSD_PHYSX_COMMAND_WAKE:
        if (!dynamic)
        {
            reject("A wake command was sent to an actor that is not dynamic.");
            return;
        }
        body->wakeUp();
        return;
    case OPENUSD_PHYSX_COMMAND_SLEEP:
        if (!dynamic)
        {
            reject("A sleep command was sent to an actor that is not dynamic.");
            return;
        }
        body->putToSleep();
        return;
    default:
        reject("The command type is not supported by this ABI version.");
        return;
    }
}

void CollectStateEvents(openusd_physx_world& world)
{
    for (WorldActor& record : world.actors)
    {
        if (record.type != OPENUSD_PHYSX_ACTOR_DYNAMIC || record.actor == nullptr)
        {
            continue;
        }
        PxRigidDynamic* body = static_cast<PxRigidDynamic*>(record.actor);
        const bool sleeping = body->isSleeping();
        if (sleeping == record.sleeping)
        {
            continue;
        }
        record.sleeping = sleeping;
        openusd_physx_event event{};
        event.id0 = record.id;
        event.step_index = world.step_index;
        event.type = sleeping
            ? static_cast<uint32_t>(OPENUSD_PHYSX_EVENT_SLEEP)
            : static_cast<uint32_t>(OPENUSD_PHYSX_EVENT_WAKE);
        PushEvent(world, event);
    }

    for (WorldJoint& record : world.joints)
    {
        if (record.joint == nullptr || record.broken)
        {
            continue;
        }
        if (!record.break_pending &&
            !record.joint->getConstraintFlags().isSet(PxConstraintFlag::eBROKEN))
        {
            continue;
        }
        record.broken = true;
        record.break_pending = false;
        openusd_physx_event event{};
        event.id0 = record.id;
        event.id1 = record.actor0_index >= 0
            ? world.actors[static_cast<size_t>(record.actor0_index)].id
            : OPENUSD_PHYSX_INVALID_ID;
        event.detail0 = record.actor1_index >= 0
            ? world.actors[static_cast<size_t>(record.actor1_index)].id
            : OPENUSD_PHYSX_INVALID_ID;
        event.step_index = world.step_index;
        event.type = static_cast<uint32_t>(OPENUSD_PHYSX_EVENT_JOINT_BREAK);
        PushEvent(world, event);
    }
}

void CollectDebugLines(openusd_physx_world& world)
{
    if ((world.flags & OPENUSD_PHYSX_WORLD_FLAG_ENABLE_DEBUG) == 0)
    {
        return;
    }
    for (PxScene* scene : world.scenes)
    {
        const PxRenderBuffer& buffer = scene->getRenderBuffer();
        const PxU32 line_count = buffer.getNbLines();
        const PxDebugLine* lines = buffer.getLines();
        for (PxU32 index = 0; index < line_count; ++index)
        {
            if (world.debug_lines.size() >= world.capacities.max_debug_lines)
            {
                if (world.dropped_debug_lines != UINT32_MAX)
                {
                    ++world.dropped_debug_lines;
                }
                world.overflow_flags |= OPENUSD_PHYSX_OVERFLOW_DEBUG_LINES;
                continue;
            }
            openusd_physx_debug_line line{};
            line.start = openusd_physx_translate::FromPx(lines[index].pos0);
            line.end = openusd_physx_translate::FromPx(lines[index].pos1);
            line.color = lines[index].color0;
            line.category = 0;
            world.debug_lines.push_back(line);
        }
    }
}

bool BufferIsConsistent(const void* pointer, size_t capacity) noexcept
{
    return (pointer == nullptr) == (capacity == 0);
}

openusd_physx_status ValidateResultPage(
    const openusd_physx_world& world,
    const openusd_physx_result_page* results,
    openusd_physx_error_buffer* error)
{
    if (results == nullptr)
    {
        WriteError(error, "The result page pointer must not be null.");
        return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
    }
    if (results->struct_size != sizeof(openusd_physx_result_page))
    {
        WriteError(error, "The result page structure size does not match this ABI.");
        return OPENUSD_PHYSX_STATUS_VERSION_MISMATCH;
    }
    if (results->abi_version != OPENUSD_PHYSX_WORLD_ABI_VERSION)
    {
        WriteError(error, "The result page ABI version does not match this ABI exactly.");
        return OPENUSD_PHYSX_STATUS_VERSION_MISMATCH;
    }
    if (!BufferIsConsistent(results->body_states, results->body_state_capacity) ||
        !BufferIsConsistent(results->events, results->event_capacity) ||
        !BufferIsConsistent(results->diagnostics, results->diagnostic_capacity) ||
        !BufferIsConsistent(results->debug_lines, results->debug_line_capacity) ||
        !BufferIsConsistent(results->deformations, results->deformation_capacity) ||
        !BufferIsConsistent(results->deformation_points, results->deformation_point_capacity))
    {
        WriteError(error, "Every result buffer pointer must be null exactly when its capacity is zero.");
        return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
    }
    // A half declared deformation window would let a caller receive body records
    // whose vertices were never written, so the pair is accepted only together.
    if ((results->deformation_capacity == 0) != (results->deformation_point_capacity == 0))
    {
        WriteError(
            error,
            "The deformation body and deformation point buffers must both be present or both be absent.");
        return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
    }
    if (results->body_state_capacity < world.dynamic_actor_count)
    {
        WriteError(error, "The body state capacity is smaller than the number of movable actors in this world.");
        return OPENUSD_PHYSX_STATUS_CAPACITY_EXCEEDED;
    }
    return OPENUSD_PHYSX_STATUS_OK;
}

// Fills the caller owned result page and clears every buffer that was consumed.
// Nothing is allocated, retained, or freed by the library.
void FillResults(openusd_physx_world& world, openusd_physx_result_page& results)
{
    size_t body_count = 0;
    for (const WorldActor& record : world.actors)
    {
        if (!IsMovable(record.type) || record.actor == nullptr)
        {
            continue;
        }
        openusd_physx_body_state state{};
        state.id = record.id;
        state.pose = openusd_physx_translate::FromPx(record.actor->getGlobalPose());
        if (record.type == OPENUSD_PHYSX_ACTOR_DYNAMIC)
        {
            PxRigidDynamic* body = static_cast<PxRigidDynamic*>(record.actor);
            state.linear_velocity = openusd_physx_translate::FromPx(body->getLinearVelocity());
            state.angular_velocity = openusd_physx_translate::FromPx(body->getAngularVelocity());
            if (body->isSleeping())
            {
                state.flags |= OPENUSD_PHYSX_BODY_STATE_FLAG_SLEEPING;
            }
        }
        else
        {
            state.flags |= OPENUSD_PHYSX_BODY_STATE_FLAG_KINEMATIC;
        }
        results.body_states[body_count] = state;
        ++body_count;
    }

    // Articulation links and controllers follow the actors in a fixed order, so
    // an identity keeps the same slot from one step to the next and a consumer
    // that reads by identity never has to care which kind produced it.
    for (const WorldArticulationLink& record : world.articulation_links)
    {
        if (record.link == nullptr)
        {
            continue;
        }
        openusd_physx_body_state state{};
        state.id = record.id;
        state.pose = openusd_physx_translate::FromPx(record.link->getGlobalPose());
        state.linear_velocity = openusd_physx_translate::FromPx(record.link->getLinearVelocity());
        state.angular_velocity = openusd_physx_translate::FromPx(record.link->getAngularVelocity());
        state.flags = OPENUSD_PHYSX_BODY_STATE_FLAG_ARTICULATION_LINK;
        const PxArticulationReducedCoordinate& owner = record.link->getArticulation();
        if (owner.isSleeping())
        {
            state.flags |= OPENUSD_PHYSX_BODY_STATE_FLAG_SLEEPING;
        }
        results.body_states[body_count] = state;
        ++body_count;
    }

    for (const WorldController& record : world.controllers)
    {
        if (record.controller == nullptr)
        {
            continue;
        }
        const PxExtendedVec3 position = record.controller->getPosition();
        openusd_physx_body_state state{};
        state.id = record.id;
        state.pose.position.x = static_cast<float>(position.x);
        state.pose.position.y = static_cast<float>(position.y);
        state.pose.position.z = static_cast<float>(position.z);
        state.pose.rotation = openusd_physx_quatf{0.0F, 0.0F, 0.0F, 1.0F};
        // A controller is moved, not simulated, so the velocity it publishes is
        // the displacement the last move actually achieved over that step.
        state.linear_velocity = openusd_physx_translate::FromPx(record.last_velocity);
        state.flags = OPENUSD_PHYSX_BODY_STATE_FLAG_CONTROLLER | OPENUSD_PHYSX_BODY_STATE_FLAG_KINEMATIC;
        results.body_states[body_count] = state;
        ++body_count;
    }

    // Wheels come last so that adding a vehicle never moves the slot of an
    // actor, link or controller identity that was already published.
    for (const WorldVehicle& record : world.vehicles)
    {
        if (!record.instance->PublishesWheels())
        {
            continue;
        }
        const uint32_t wheel_count = record.instance->WheelCount();
        for (uint32_t wheel = 0; wheel < wheel_count; ++wheel)
        {
            openusd_physx_body_state state{};
            state.id = record.instance->WheelId(wheel);
            state.pose = openusd_physx_translate::FromPx(record.instance->WheelPose(wheel));
            state.angular_velocity =
                openusd_physx_translate::FromPx(record.instance->WheelVelocity(wheel));
            state.flags = OPENUSD_PHYSX_BODY_STATE_FLAG_VEHICLE_WHEEL;
            results.body_states[body_count] = state;
            ++body_count;
        }
    }

    // Deformation output is published before the diagnostics are copied, because
    // a body that did not fit raises an overflow flag the same fill has to
    // report.
    uint32_t deformation_body_count = 0;
    uint32_t deformation_point_count = 0;
    uint32_t dropped_deformation_bodies = 0;
    openusd_physx_gpu::Publish(
        world.gpu,
        results.deformations,
        results.deformation_capacity,
        results.deformation_points,
        results.deformation_point_capacity,
        deformation_body_count,
        deformation_point_count,
        dropped_deformation_bodies);

    // The sink is ordered exactly once per fill, so the caller always receives
    // the deterministic prefix of the total event order, even when the caller
    // owned page is smaller than the sink capacity.
    world.event_sink.Sort();
    const size_t event_count = std::min(world.event_sink.Size(), results.event_capacity);
    for (size_t index = 0; index < event_count; ++index)
    {
        results.events[index] = world.event_sink.Data()[index];
    }
    const size_t diagnostic_count = std::min(world.diagnostics.size(), results.diagnostic_capacity);
    for (size_t index = 0; index < diagnostic_count; ++index)
    {
        results.diagnostics[index] = world.diagnostics[index];
    }
    const size_t debug_line_count = std::min(world.debug_lines.size(), results.debug_line_capacity);
    for (size_t index = 0; index < debug_line_count; ++index)
    {
        results.debug_lines[index] = world.debug_lines[index];
    }

    uint32_t overflow = world.overflow_flags;
    const size_t dropped_events = world.event_sink.Size() - event_count;
    const size_t dropped_diagnostics = world.diagnostics.size() - diagnostic_count;
    const size_t dropped_debug_lines = world.debug_lines.size() - debug_line_count;
    if (dropped_events != 0)
    {
        overflow |= OPENUSD_PHYSX_OVERFLOW_EVENTS;
    }
    if (dropped_diagnostics != 0)
    {
        overflow |= OPENUSD_PHYSX_OVERFLOW_DIAGNOSTICS;
    }
    if (dropped_debug_lines != 0)
    {
        overflow |= OPENUSD_PHYSX_OVERFLOW_DEBUG_LINES;
    }
    if (dropped_deformation_bodies != 0)
    {
        overflow |= OPENUSD_PHYSX_OVERFLOW_DEFORMATION;
    }

    results.header = openusd_physx_result_header{};
    results.header.revision = world.revision;
    results.header.step_index = world.step_index;
    results.header.simulation_time = world.simulation_time;
    results.header.last_step_seconds = world.last_step_seconds;
    results.header.total_step_seconds = world.total_step_seconds;
    results.header.body_state_count = static_cast<uint32_t>(body_count);
    results.header.event_count = static_cast<uint32_t>(event_count);
    results.header.diagnostic_count = static_cast<uint32_t>(diagnostic_count);
    results.header.debug_line_count = static_cast<uint32_t>(debug_line_count);
    results.header.dropped_event_count = static_cast<uint32_t>(
        std::min<size_t>(
            static_cast<size_t>(world.event_sink.Dropped()) + dropped_events,
            UINT32_MAX));
    results.header.dropped_diagnostic_count = static_cast<uint32_t>(
        std::min<size_t>(world.dropped_diagnostics + dropped_diagnostics, UINT32_MAX));
    results.header.dropped_debug_line_count = static_cast<uint32_t>(
        std::min<size_t>(world.dropped_debug_lines + dropped_debug_lines, UINT32_MAX));
    results.header.overflow_flags = overflow;
    results.header.state = world.state;
    results.header.deformation_body_count = deformation_body_count;
    results.header.deformation_point_count = deformation_point_count;
    results.header.dropped_deformation_body_count = dropped_deformation_bodies;

    ClearResultBuffers(world);
}

// Advances every character controller by the displacement its commands asked
// for plus the gravity it opted into. A controller is swept, not simulated, so
// this runs after the scene has been stepped and the geometry it sweeps against
// is already in its post step pose.
void MoveControllers(openusd_physx_world& world, double step_seconds)
{
    if (world.controllers.empty())
    {
        return;
    }
    const PxReal dt = static_cast<PxReal>(step_seconds);
    const PxControllerFilters filters;
    for (WorldController& record : world.controllers)
    {
        if (record.controller == nullptr)
        {
            continue;
        }
        PxVec3 displacement = record.pending_move;
        record.pending_move = PxVec3(0.0F, 0.0F, 0.0F);
        record.has_pending_move = false;
        if ((record.flags & OPENUSD_PHYSX_CONTROLLER_FLAG_APPLY_GRAVITY) != 0)
        {
            // The fall velocity is integrated here because a controller has no
            // rigid body state PhysX could integrate it into.
            record.fall_velocity += world.scenes[record.scene_index]->getGravity() * dt;
            displacement += record.fall_velocity * dt;
        }

        const PxExtendedVec3 before = record.controller->getPosition();
        const PxControllerCollisionFlags collision =
            record.controller->move(displacement, 0.0F, dt, filters);
        record.grounded = collision.isSet(PxControllerCollisionFlag::eCOLLISION_DOWN);
        if (record.grounded || collision.isSet(PxControllerCollisionFlag::eCOLLISION_UP))
        {
            record.fall_velocity = PxVec3(0.0F, 0.0F, 0.0F);
        }
        const PxExtendedVec3 after = record.controller->getPosition();
        record.last_velocity = PxVec3(
            static_cast<float>((after.x - before.x) / step_seconds),
            static_cast<float>((after.y - before.y) / step_seconds),
            static_cast<float>((after.z - before.z) / step_seconds));
    }
}

// Runs every vehicle for one substep. A vehicle resolves its own road queries,
// suspension, tire and drivetrain forces and writes the result onto its chassis
// actor, so this must happen before the scene is simulated.
void StepVehicles(openusd_physx_world& world, double step_seconds)
{
    if (world.vehicles.empty())
    {
        return;
    }
    for (WorldVehicle& record : world.vehicles)
    {
        record.instance->SetCommands(
            record.throttle, record.brake, record.hand_brake, record.steer, record.clutch, record.gear);
        record.instance->Step(
            static_cast<PxReal>(step_seconds),
            world.scenes[static_cast<size_t>(record.scene_index)]->getGravity());
        const uint32_t gear = record.instance->CurrentGear();
        if (gear != record.last_gear)
        {
            openusd_physx_event event{};
            event.id0 = record.id;
            event.detail0 = record.last_gear;
            event.detail1 = gear;
            event.step_index = world.step_index;
            event.type = static_cast<uint32_t>(OPENUSD_PHYSX_EVENT_VEHICLE_GEAR_CHANGE);
            event.impulse = record.instance->EngineSpeed();
            PushEvent(world, event);
            record.last_gear = gear;
        }
    }
}

openusd_physx_status StepWorld(
    openusd_physx_world& world,
    const openusd_physx_step_desc& desc,
    openusd_physx_error_buffer* error)
{
    if (world.state != OPENUSD_PHYSX_WORLD_STATE_READY)
    {
        WriteError(error, "The world must hold a successfully built page before it can step.");
        return OPENUSD_PHYSX_STATUS_INVALID_STATE;
    }
    if (desc.flags != 0 || desc.reserved != 0)
    {
        WriteError(error, "The step description declares unknown flags or non zero reserved fields.");
        return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
    }
    if (!BufferIsConsistent(desc.commands, desc.command_count))
    {
        WriteError(error, "The command pointer must be null exactly when the command count is zero.");
        return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
    }

    double step_seconds = desc.fixed_time_step;
    if (step_seconds == 0.0)
    {
        step_seconds = world.default_time_step;
    }
    else
    {
        const double fastest = 1.0 / static_cast<double>(OPENUSD_PHYSX_MAX_SIMULATION_RATE_HZ);
        const double slowest = 1.0 / static_cast<double>(OPENUSD_PHYSX_MIN_SIMULATION_RATE_HZ);
        if (!std::isfinite(step_seconds) || step_seconds < fastest || step_seconds > slowest)
        {
            WriteError(error, "The fixed time step must be zero or inside the supported simulation rate range.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
    }

    const uint32_t substeps = desc.substep_count == 0 ? 1U : desc.substep_count;
    if (substeps > world.max_substeps)
    {
        WriteError(error, "The requested substep count exceeds the maximum declared by the build page.");
        return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
    }

    std::string reason;
    for (size_t index = 0; index < desc.command_count; ++index)
    {
        if (!openusd_physx_events::ValidateCommand(desc.commands[index], index, reason))
        {
            WriteError(error, reason);
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
    }

    const std::chrono::steady_clock::time_point started = std::chrono::steady_clock::now();
    for (size_t index = 0; index < desc.command_count; ++index)
    {
        ApplyCommand(world, desc.commands[index]);
    }

    for (uint32_t substep = 0; substep < substeps; ++substep)
    {
        // The index is raised before the substep runs, so the simulation event
        // callbacks that fire inside fetchResults and the deterministic scan
        // that follows them stamp one and the same index. The result header
        // reports the index of the last substep, which is therefore also the
        // index every event of that substep carries.
        ++world.step_index;
        // Vehicles run before the scene so the suspension, tire and drivetrain
        // forces they resolve are part of the very substep the solver sees.
        StepVehicles(world, step_seconds);
        for (PxScene* scene : world.scenes)
        {
            scene->simulate(static_cast<PxReal>(step_seconds));
            scene->fetchResults(true);
        }
        world.simulation_time += step_seconds;
        MoveControllers(world, step_seconds);
        CollectStateEvents(world);
    }
    CollectDebugLines(world);

    const std::chrono::duration<double> elapsed = std::chrono::steady_clock::now() - started;
    world.last_step_seconds = elapsed.count();
    world.total_step_seconds += world.last_step_seconds;
    return OPENUSD_PHYSX_STATUS_OK;
}

void RestoreActor(WorldActor& record, const openusd_physx_body_state& state)
{
    const PxTransform pose = openusd_physx_translate::ToPx(state.pose);
    if (record.type == OPENUSD_PHYSX_ACTOR_KINEMATIC)
    {
        PxRigidDynamic* body = static_cast<PxRigidDynamic*>(record.actor);
        body->setGlobalPose(pose);
        body->setKinematicTarget(pose);
        record.sleeping = false;
        return;
    }
    if (record.type == OPENUSD_PHYSX_ACTOR_DYNAMIC)
    {
        PxRigidDynamic* body = static_cast<PxRigidDynamic*>(record.actor);
        body->setGlobalPose(pose);
        body->clearForce(PxForceMode::eFORCE);
        body->clearTorque(PxForceMode::eFORCE);
        body->setLinearVelocity(openusd_physx_translate::ToPx(state.linear_velocity));
        body->setAngularVelocity(openusd_physx_translate::ToPx(state.angular_velocity));
        if ((state.flags & OPENUSD_PHYSX_BODY_STATE_FLAG_SLEEPING) != 0)
        {
            body->putToSleep();
            record.sleeping = true;
        }
        else
        {
            body->wakeUp();
            record.sleeping = false;
        }
        return;
    }
    record.actor->setGlobalPose(pose);
}

openusd_physx_status ResetWorld(
    openusd_physx_world& world,
    const openusd_physx_reset_desc* desc,
    openusd_physx_error_buffer* error)
{
    if (world.state != OPENUSD_PHYSX_WORLD_STATE_READY)
    {
        WriteError(error, "The world must hold a successfully built page before it can reset.");
        return OPENUSD_PHYSX_STATUS_INVALID_STATE;
    }

    double simulation_time = 0.0;
    if (desc != nullptr)
    {
        if (desc->struct_size != sizeof(openusd_physx_reset_desc))
        {
            WriteError(error, "The reset description structure size does not match this ABI.");
            return OPENUSD_PHYSX_STATUS_VERSION_MISMATCH;
        }
        if (desc->flags != 0)
        {
            WriteError(error, "The reset description declares unknown flags.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        if (!BufferIsConsistent(desc->body_states, desc->body_state_count))
        {
            WriteError(error, "The body state pointer must be null exactly when the body state count is zero.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        if (!std::isfinite(desc->simulation_time) || desc->simulation_time < 0.0)
        {
            WriteError(error, "The reset simulation time must be finite and not negative.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        simulation_time = desc->simulation_time;

        for (size_t index = 0; index < desc->body_state_count; ++index)
        {
            const openusd_physx_body_state& state = desc->body_states[index];
            if (state.reserved0 != 0 || state.reserved1 != 0 ||
                (state.flags & ~static_cast<uint32_t>(OPENUSD_PHYSX_BODY_STATE_FLAG_ALL)) != 0)
            {
                WriteError(error, "A reset body state declares unknown flags or non zero reserved fields.");
                return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
            }
            if (!IsFinite(state.pose) || !IsUsableRotation(state.pose.rotation) ||
                !IsFinite(state.linear_velocity) || !IsFinite(state.angular_velocity))
            {
                WriteError(error, "A reset body state declares a non finite pose or velocity.");
                return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
            }
            const auto entry = world.actor_by_id.find(state.id);
            if (entry == world.actor_by_id.end() || !IsMovable(world.actors[entry->second].type))
            {
                WriteError(error, "A reset body state targets an identity that is not a movable actor of this world.");
                return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
            }
        }
    }

    for (WorldActor& record : world.actors)
    {
        if (record.actor == nullptr)
        {
            continue;
        }
        RestoreActor(record, record.initial);
    }

    // An articulation returns to its build pose through its root, because the
    // reduced coordinates are what place every other link. Every joint position
    // and velocity is cleared so the pose is reproduced exactly.
    for (WorldArticulation& articulation_record : world.articulations)
    {
        if (articulation_record.articulation == nullptr || articulation_record.link_count == 0)
        {
            continue;
        }
        for (size_t slot = 0; slot < articulation_record.link_count; ++slot)
        {
            WorldArticulationLink& link_record =
                world.articulation_links[articulation_record.link_offset + slot];
            if (link_record.link == nullptr)
            {
                continue;
            }
            PxArticulationJointReducedCoordinate* joint = link_record.link->getInboundJoint();
            if (joint == nullptr)
            {
                continue;
            }
            for (size_t axis = 0; axis < OPENUSD_PHYSX_JOINT_AXIS_COUNT; ++axis)
            {
                const PxArticulationAxis::Enum px_axis = ToArticulationAxis(axis);
                if (joint->getMotion(px_axis) == PxArticulationMotion::eLOCKED)
                {
                    continue;
                }
                joint->setJointPosition(px_axis, 0.0F);
                joint->setJointVelocity(px_axis, 0.0F);
            }
        }
        const WorldArticulationLink& root = world.articulation_links[articulation_record.link_offset];
        articulation_record.articulation->setRootGlobalPose(
            openusd_physx_translate::ToPx(root.initial.pose),
            false);
        articulation_record.articulation->setRootLinearVelocity(PxVec3(0.0F, 0.0F, 0.0F), false);
        articulation_record.articulation->setRootAngularVelocity(PxVec3(0.0F, 0.0F, 0.0F), false);
    }

    for (WorldController& controller_record : world.controllers)
    {
        if (controller_record.controller == nullptr)
        {
            continue;
        }
        const openusd_physx_vec3f& start = controller_record.initial.pose.position;
        controller_record.controller->setPosition(PxExtendedVec3(
            static_cast<PxExtended>(start.x),
            static_cast<PxExtended>(start.y),
            static_cast<PxExtended>(start.z)));
        controller_record.fall_velocity = PxVec3(0.0F, 0.0F, 0.0F);
        controller_record.last_velocity = PxVec3(0.0F, 0.0F, 0.0F);
        controller_record.pending_move = PxVec3(0.0F, 0.0F, 0.0F);
        controller_record.has_pending_move = false;
        controller_record.grounded = false;
    }

    // A vehicle keeps drivetrain and suspension state outside its chassis actor,
    // so restoring the actor alone would leave the engine spinning.
    for (WorldVehicle& vehicle_record : world.vehicles)
    {
        vehicle_record.instance->Reset();
        vehicle_record.throttle = 0.0F;
        vehicle_record.brake = 0.0F;
        vehicle_record.hand_brake = 0.0F;
        vehicle_record.steer = 0.0F;
        vehicle_record.clutch = 0.0F;
        vehicle_record.gear = 0;
        vehicle_record.last_gear = vehicle_record.instance->CurrentGear();
    }

    if (desc != nullptr)
    {
        for (size_t index = 0; index < desc->body_state_count; ++index)
        {
            const openusd_physx_body_state& state = desc->body_states[index];
            WorldActor& record = world.actors[world.actor_by_id.find(state.id)->second];
            RestoreActor(record, state);
        }
    }

    // Broken joints are recreated from the retained descriptions so a reset
    // restores the constraint topology of the build page exactly.
    std::string reason;
    const openusd_physx_runtime::FactoryLock factory_lock;
    for (WorldJoint& record : world.joints)
    {
        if (!record.broken)
        {
            continue;
        }
        if (record.joint != nullptr)
        {
            record.joint->release();
            record.joint = nullptr;
        }
        PxRigidActor* actor0 = record.actor0_index >= 0
            ? world.actors[static_cast<size_t>(record.actor0_index)].actor
            : nullptr;
        PxRigidActor* actor1 = record.actor1_index >= 0
            ? world.actors[static_cast<size_t>(record.actor1_index)].actor
            : nullptr;
        reason.clear();
        record.joint = CreateJoint(record.desc, actor0, actor1, reason);
        if (record.joint == nullptr)
        {
            WriteError(error, "A broken joint could not be recreated during reset. " + reason);
            return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
        }
        record.joint->userData = reinterpret_cast<void*>(
            static_cast<uintptr_t>(
                static_cast<size_t>(&record - world.joints.data()) + 1));
        record.broken = false;
        record.break_pending = false;
    }

    // Every CUDA backed object is restored to the vertex configuration the build
    // captured, exactly like a rigid body is restored to its built pose.
    openusd_physx_gpu::Reset(world.gpu);

    ClearResultBuffers(world);
    world.step_index = 0;
    world.simulation_time = simulation_time;
    world.last_step_seconds = 0.0;
    world.total_step_seconds = 0.0;
    return OPENUSD_PHYSX_STATUS_OK;
}

void FillHitGeometry(openusd_physx_query_hit& hit, const PxRaycastHit& source) noexcept
{
    if (source.flags.isSet(PxHitFlag::ePOSITION))
    {
        hit.position = openusd_physx_translate::FromPx(source.position);
        hit.flags |= OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_POSITION;
    }
    if (source.flags.isSet(PxHitFlag::eNORMAL))
    {
        hit.normal = openusd_physx_translate::FromPx(source.normal);
        hit.flags |= OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_NORMAL;
    }
    hit.distance = source.distance;
    hit.flags |= OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_DISTANCE;
    hit.face_index = source.faceIndex;
    if (source.faceIndex != 0xFFFFFFFFU)
    {
        hit.flags |= OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_FACE;
    }
}

void FillHitGeometry(openusd_physx_query_hit& hit, const PxSweepHit& source) noexcept
{
    if (source.hadInitialOverlap())
    {
        // PhysX leaves the position undefined and substitutes the negated sweep
        // direction for the normal when the sweep starts already touching, so
        // the hit reports the contract's zero distance and no geometry at all
        // instead of a fabricated pose.
        hit.distance = 0.0F;
        hit.flags |= OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_DISTANCE |
            OPENUSD_PHYSX_QUERY_HIT_FLAG_INITIAL_OVERLAP;
    }
    else
    {
        if (source.flags.isSet(PxHitFlag::ePOSITION))
        {
            hit.position = openusd_physx_translate::FromPx(source.position);
            hit.flags |= OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_POSITION;
        }
        if (source.flags.isSet(PxHitFlag::eNORMAL))
        {
            hit.normal = openusd_physx_translate::FromPx(source.normal);
            hit.flags |= OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_NORMAL;
        }
        hit.distance = source.distance;
        hit.flags |= OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_DISTANCE;
    }
    hit.face_index = source.faceIndex;
    if (source.faceIndex != 0xFFFFFFFFU)
    {
        hit.flags |= OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_FACE;
    }
}

void FillHitGeometry(openusd_physx_query_hit& hit, const PxOverlapHit& source) noexcept
{
    // An overlap reports no pose, no normal, and no distance, so only the face
    // identity is populated and the hit flags stay clear for everything else.
    hit.face_index = source.faceIndex;
    if (source.faceIndex != 0xFFFFFFFFU)
    {
        hit.flags |= OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_FACE;
    }
}

bool MakeQueryGeometry(const openusd_physx_query_request& request, PxGeometryHolder& holder, std::string& reason)
{
    switch (request.shape_type)
    {
    case OPENUSD_PHYSX_SHAPE_SPHERE:
        if (!(request.radius > 0.0F) || !std::isfinite(request.radius))
        {
            reason = "The query sphere radius must be positive and finite.";
            return false;
        }
        holder = PxSphereGeometry(request.radius);
        return true;
    case OPENUSD_PHYSX_SHAPE_BOX:
        if (!IsFinite(request.half_extents) ||
            !(request.half_extents.x > 0.0F) || !(request.half_extents.y > 0.0F) || !(request.half_extents.z > 0.0F))
        {
            reason = "The query box half extents must be positive and finite.";
            return false;
        }
        holder = PxBoxGeometry(request.half_extents.x, request.half_extents.y, request.half_extents.z);
        return true;
    case OPENUSD_PHYSX_SHAPE_CAPSULE:
        if (!(request.radius > 0.0F) || !std::isfinite(request.radius) ||
            !(request.half_height > 0.0F) || !std::isfinite(request.half_height))
        {
            reason = "The query capsule radius and half height must be positive and finite.";
            return false;
        }
        holder = PxCapsuleGeometry(request.radius, request.half_height);
        return true;
    default:
        reason = "Only sphere, box, and capsule geometry is supported by sweep and overlap queries.";
        return false;
    }
}

// Builds one ABI hit from one PhysX hit. The hit carries no stage handle and no
// PhysX pointer: only the identities the build page declared.
template <typename THit>
openusd_physx_query_hit MakeHit(
    const openusd_physx_world& world,
    const openusd_physx_query_request& request,
    const THit& source)
{
    openusd_physx_query_hit hit{};
    hit.user_id = request.user_id;
    hit.actor_id = ActorIdOf(world, source.actor);
    hit.shape_id = ShapeIdOf(world, source.shape);
    if (source.shape != nullptr && source.shape->getFlags().isSet(PxShapeFlag::eTRIGGER_SHAPE))
    {
        hit.flags |= OPENUSD_PHYSX_QUERY_HIT_FLAG_TRIGGER;
    }
    FillHitGeometry(hit, source);
    return hit;
}

// Only a sweep can start already touching in a way the ABI reports; a raycast
// that starts inside a shape and an overlap are ordinary hits.
inline bool IsSweepInitialOverlap(const PxSweepHit& source) noexcept
{
    return source.hadInitialOverlap();
}

inline bool IsSweepInitialOverlap(const PxRaycastHit&) noexcept
{
    return false;
}

inline bool IsSweepInitialOverlap(const PxOverlapHit&) noexcept
{
    return false;
}

// Applies the request filters that PhysX cannot express through its own filter
// data: the collision group mask, the trigger exclusion, and the initial
// overlap exclusion.
template <typename THit>
bool HitPassesFilter(const openusd_physx_query_request& request, const THit& source) noexcept
{
    if (IsSweepInitialOverlap(source) &&
        (request.flags & OPENUSD_PHYSX_QUERY_FLAG_SWEEP_INITIAL_OVERLAP) == 0)
    {
        return false;
    }
    if (source.shape == nullptr)
    {
        return request.filter_mask == 0;
    }
    if (request.filter_mask != 0 &&
        (source.shape->getQueryFilterData().word0 & request.filter_mask) == 0)
    {
        return false;
    }
    if ((request.flags & OPENUSD_PHYSX_QUERY_FLAG_EXCLUDE_TRIGGERS) != 0 &&
        source.shape->getFlags().isSet(PxShapeFlag::eTRIGGER_SHAPE))
    {
        return false;
    }
    return true;
}

// Drains one PhysX hit buffer into the per request sink. The sink owns the
// bounded, deterministic retention policy, so this loop only filters and maps.
// Returns true when PhysX filled its own touch buffer, in which case it may
// have discarded an unknown number of touches of its own choosing and no exact
// dropped count can be claimed for this request.
template <typename TBuffer>
bool DrainBuffer(
    const openusd_physx_world& world,
    const openusd_physx_query_request& request,
    const TBuffer& buffer,
    openusd_physx_events::HitSink& sink)
{
    for (PxU32 index = 0; index < buffer.getNbAnyHits(); ++index)
    {
        const auto& source = buffer.getAnyHit(index);
        if (!HitPassesFilter(request, source))
        {
            continue;
        }
        sink.Retain(MakeHit(world, request, source));
    }
    return buffer.getMaxNbTouches() != 0 && buffer.getNbTouches() >= buffer.getMaxNbTouches();
}

openusd_physx_status RunQueries(
    openusd_physx_world& world,
    const openusd_physx_query_desc& desc,
    openusd_physx_query_result& result,
    openusd_physx_error_buffer* error)
{
    if (world.state != OPENUSD_PHYSX_WORLD_STATE_READY)
    {
        WriteError(error, "The world must hold a successfully built page before it can run queries.");
        return OPENUSD_PHYSX_STATUS_INVALID_STATE;
    }
    if (!BufferIsConsistent(desc.requests, desc.request_count) ||
        !BufferIsConsistent(desc.hits, desc.hit_capacity))
    {
        WriteError(error, "Every query pointer must be null exactly when its count or capacity is zero.");
        return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
    }

    size_t hit_count = 0;
    size_t dropped = 0;
    size_t rejected = 0;
    bool truncated = false;
    const auto reject = [&](uint64_t user_id, const std::string& message)
    {
        ++rejected;
        PushDiagnostic(
            world,
            OPENUSD_PHYSX_DIAGNOSTIC_WARNING,
            OPENUSD_PHYSX_DIAGNOSTIC_QUERY_REJECTED,
            user_id,
            message);
    };

    std::string reason;
    for (size_t index = 0; index < desc.request_count; ++index)
    {
        const openusd_physx_query_request request = desc.requests[index];
        reason.clear();
        if (!openusd_physx_events::ValidateQueryRequest(request, world.scenes.size(), reason))
        {
            reject(request.user_id, reason);
            continue;
        }

        const bool exclude_static = (request.flags & OPENUSD_PHYSX_QUERY_FLAG_EXCLUDE_STATIC) != 0;
        const bool exclude_dynamic = (request.flags & OPENUSD_PHYSX_QUERY_FLAG_EXCLUDE_DYNAMIC) != 0;
        const bool any_hit = (request.flags & OPENUSD_PHYSX_QUERY_FLAG_ANY_HIT) != 0;

        PxQueryFlags query_flags = PxQueryFlags(0);
        if (!exclude_static)
        {
            query_flags |= PxQueryFlag::eSTATIC;
        }
        if (!exclude_dynamic)
        {
            query_flags |= PxQueryFlag::eDYNAMIC;
        }
        if (any_hit)
        {
            query_flags |= PxQueryFlag::eANY_HIT;
        }
        const PxQueryFilterData filter(query_flags);
        PxScene* scene = world.scenes[static_cast<size_t>(request.scene_index)];
        const PxVec3 origin = openusd_physx_translate::ToPx(request.origin);

        // PhysX discards an arbitrary subset once its own touch buffer is full,
        // so it is always handed the whole scratch regardless of how few hits
        // this request wants to keep. Narrowing it to max_hits would let PhysX,
        // not the deterministic sink, decide which hits survive.
        const size_t budget = kQueryTouchBuffer;

        // The sink writes straight into the caller owned array, so a request
        // that overflows its own budget or the remaining page capacity keeps
        // the nearest hits and never copies a dropped hit anywhere.
        const size_t remaining = desc.hit_capacity - std::min(hit_count, desc.hit_capacity);
        openusd_physx_events::HitSink sink(
            remaining == 0 ? nullptr : desc.hits + hit_count,
            std::min<size_t>(request.max_hits, remaining));
        bool request_truncated = false;

        if (request.type == OPENUSD_PHYSX_QUERY_OVERLAP)
        {
            PxGeometryHolder holder;
            reason.clear();
            if (!MakeQueryGeometry(request, holder, reason))
            {
                reject(request.user_id, reason);
                continue;
            }
            const PxTransform pose(origin, openusd_physx_translate::ToPx(request.rotation).getNormalized());
            PxOverlapBuffer buffer(world.overlap_scratch.data(), static_cast<PxU32>(budget));
            scene->overlap(holder.any(), pose, buffer, filter);
            request_truncated = DrainBuffer(world, request, buffer, sink);
        }
        else if (request.type == OPENUSD_PHYSX_QUERY_RAYCAST)
        {
            const PxVec3 unit_direction = openusd_physx_translate::ToPx(request.direction).getNormalized();
            PxRaycastBuffer buffer(world.raycast_scratch.data(), static_cast<PxU32>(budget));
            scene->raycast(origin, unit_direction, request.max_distance, buffer, PxHitFlag::eDEFAULT, filter);
            request_truncated = DrainBuffer(world, request, buffer, sink);
        }
        else
        {
            PxGeometryHolder holder;
            reason.clear();
            if (!MakeQueryGeometry(request, holder, reason))
            {
                reject(request.user_id, reason);
                continue;
            }
            // The minimum translational distance is deliberately not requested:
            // it turns an initially overlapping sweep into a negative distance
            // with a fabricated position, which the ABI contract forbids. An
            // initially overlapping sweep is instead either dropped or reported
            // with a zero distance and no geometry.
            const PxHitFlags hit_flags = PxHitFlag::eDEFAULT;
            const PxVec3 unit_direction = openusd_physx_translate::ToPx(request.direction).getNormalized();
            const PxTransform pose(origin, openusd_physx_translate::ToPx(request.rotation).getNormalized());
            PxSweepBuffer buffer(world.sweep_scratch.data(), static_cast<PxU32>(budget));
            scene->sweep(holder.any(), pose, unit_direction, request.max_distance, buffer, hit_flags, filter);
            request_truncated = DrainBuffer(world, request, buffer, sink);
        }

        sink.Sort();
        hit_count += sink.Size();
        dropped += sink.Dropped();
        if (request_truncated)
        {
            truncated = true;
            PushDiagnostic(
                world,
                OPENUSD_PHYSX_DIAGNOSTIC_WARNING,
                OPENUSD_PHYSX_DIAGNOSTIC_RESULT_OVERFLOW,
                request.user_id,
                "The simulation SDK filled its own touch buffer for this request, so the reported "
                "dropped hit count is a lower bound and the retained hits are not guaranteed to be "
                "the globally nearest ones.");
        }
    }

    result.hit_count = hit_count;
    result.dropped_hit_count = dropped;
    result.rejected_request_count = rejected;
    result.overflow_flags = 0U;
    if (dropped != 0 || truncated)
    {
        result.overflow_flags |= static_cast<uint32_t>(OPENUSD_PHYSX_OVERFLOW_QUERY_HITS);
    }
    if (truncated)
    {
        result.overflow_flags |= static_cast<uint32_t>(OPENUSD_PHYSX_OVERFLOW_QUERY_TRUNCATED);
    }
    if (dropped != 0)
    {
        PushDiagnostic(
            world,
            OPENUSD_PHYSX_DIAGNOSTIC_WARNING,
            OPENUSD_PHYSX_DIAGNOSTIC_RESULT_OVERFLOW,
            OPENUSD_PHYSX_INVALID_ID,
            "The caller supplied hit capacity was too small for every reported hit.");
    }
    return OPENUSD_PHYSX_STATUS_OK;
}
}

openusd_physx_status openusd_physx_world_get_capabilities(
    uint32_t abi_version,
    openusd_physx_capabilities* capabilities,
    openusd_physx_error_buffer* error)
{
    return Guard(error, [&]() -> openusd_physx_status
    {
        if (capabilities == nullptr)
        {
            WriteError(error, "The capabilities pointer must not be null.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        if (abi_version != OPENUSD_PHYSX_WORLD_ABI_VERSION ||
            capabilities->struct_size != sizeof(openusd_physx_capabilities))
        {
            WriteError(error, "The capabilities ABI version or structure size does not match this ABI exactly.");
            return OPENUSD_PHYSX_STATUS_VERSION_MISMATCH;
        }
        capabilities->abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION;
        capabilities->flags =
            static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_CPU_RIGID_BODIES) |
            static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_MESH_COOKING) |
            static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_JOINTS) |
            static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_SCENE_QUERIES) |
            static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_SLEEP_EVENTS) |
            static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_JOINT_BREAK_EVENTS) |
            static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_CONTACT_EVENTS) |
            static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_TRIGGER_EVENTS) |
            static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_BATCHED_QUERIES) |
            static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_CONVEX_CORE_SHAPES) |
            static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_HEIGHTFIELD_SHAPES) |
            static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_D6_JOINT_DRIVES) |
            static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_SHAPE_OFFSETS) |
            static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_RIGID_BODY_TUNING) |
            static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_ARTICULATIONS) |
            static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_CHARACTER_CONTROLLERS) |
            static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_ARTICULATION_TENDONS) |
            static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_ARTICULATION_MIMIC_JOINTS) |
            static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_VEHICLES) |
            static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_DEBUG_LINES);
        // The CUDA bits are only published once a context manager has actually
        // been created and has reported a usable device. They are never
        // published because the library was compiled with GPU support, because
        // a caller that reads them is deciding whether to author objects that
        // would otherwise be skipped one by one at build time.
        std::string cuda_reason;
        if (openusd_physx_cuda::Acquire(cuda_reason) != nullptr)
        {
            capabilities->flags |=
                static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_GPU_DOMAINS) |
                static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_CUDA_CONTEXT) |
                static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_PARTICLE_SYSTEMS) |
                static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_SURFACE_DEFORMABLES) |
                static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_VOLUME_DEFORMABLES) |
                static_cast<uint32_t>(OPENUSD_PHYSX_CAPABILITY_DEFORMATION_RESULTS);
        }
        capabilities->physx_version_major = PX_PHYSICS_VERSION_MAJOR;
        capabilities->physx_version_minor = PX_PHYSICS_VERSION_MINOR;
        capabilities->physx_version_bugfix = PX_PHYSICS_VERSION_BUGFIX;
        capabilities->max_scenes = OPENUSD_PHYSX_MAX_SCENES;
        capabilities->max_collision_groups = OPENUSD_PHYSX_MAX_COLLISION_GROUPS;
        capabilities->min_simulation_rate_hz = OPENUSD_PHYSX_MIN_SIMULATION_RATE_HZ;
        capabilities->max_simulation_rate_hz = OPENUSD_PHYSX_MAX_SIMULATION_RATE_HZ;
        capabilities->max_substeps = OPENUSD_PHYSX_MAX_SUBSTEPS;
        capabilities->max_result_capacity = OPENUSD_PHYSX_MAX_RESULT_CAPACITY;
        return OPENUSD_PHYSX_STATUS_OK;
    });
}

openusd_physx_status openusd_physx_world_create(
    const openusd_physx_world_desc* desc,
    openusd_physx_world** world,
    openusd_physx_error_buffer* error)
{
    return Guard(error, [&]() -> openusd_physx_status
    {
        if (world == nullptr)
        {
            WriteError(error, "The world output pointer must not be null.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        *world = nullptr;
        if (desc == nullptr)
        {
            WriteError(error, "The world description pointer must not be null.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        if (desc->struct_size != sizeof(openusd_physx_world_desc) ||
            desc->abi_version != OPENUSD_PHYSX_WORLD_ABI_VERSION)
        {
            WriteError(error, "The world description ABI version or structure size does not match this ABI exactly.");
            return OPENUSD_PHYSX_STATUS_VERSION_MISMATCH;
        }
        if ((desc->flags & ~static_cast<uint32_t>(OPENUSD_PHYSX_WORLD_FLAG_ALL)) != 0 ||
            desc->reserved0 != 0 || desc->reserved1 != 0)
        {
            WriteError(error, "The world description declares unknown flags or non zero reserved fields.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        if (desc->worker_thread_count > OPENUSD_PHYSX_MAX_SCENES)
        {
            WriteError(error, "The requested worker thread count is larger than this runtime supports.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }

        std::unique_ptr<openusd_physx_world> instance(new openusd_physx_world());
        std::string reason;
        if (!instance->runtime.Acquire(reason))
        {
            WriteError(error, reason);
            return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
        }
        instance->flags = desc->flags;
        instance->worker_thread_count = desc->worker_thread_count;
        instance->dispatcher = PxDefaultCpuDispatcherCreate(desc->worker_thread_count);
        if (instance->dispatcher == nullptr)
        {
            WriteError(error, "PhysX could not create the CPU dispatcher for this world.");
            return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
        }
        instance->raycast_scratch.resize(kQueryTouchBuffer);
        instance->sweep_scratch.resize(kQueryTouchBuffer);
        instance->overlap_scratch.resize(kQueryTouchBuffer);
        *world = instance.release();
        return OPENUSD_PHYSX_STATUS_OK;
    });
}

void openusd_physx_world_release(openusd_physx_world* world)
{
    if (world == nullptr)
    {
        return;
    }
    try
    {
        TeardownContent(*world);
        if (world->dispatcher != nullptr)
        {
            // The CPU dispatcher belongs to this world, not to the shared
            // factory, and TeardownContent has already dropped the factory
            // lock at this point.
            world->dispatcher->release();
            world->dispatcher = nullptr;
        }
    }
    catch (...)
    {
        // Release never propagates an exception to the caller.
    }
    // Destroys the runtime reference, which may take the runtime lifetime lock,
    // so it must run after every factory lock above has been dropped.
    delete world;
}

openusd_physx_status openusd_physx_world_build(
    openusd_physx_world* world,
    const void* page,
    size_t page_size,
    openusd_physx_page_validation* validation,
    openusd_physx_error_buffer* error)
{
    return Guard(error, [&]() -> openusd_physx_status
    {
        if (world == nullptr)
        {
            WriteError(error, "The world pointer must not be null.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        const std::lock_guard<std::mutex> lock(world->mutex);
        TeardownContent(*world);

        openusd_physx_page::View view;
        const openusd_physx_status status = openusd_physx_page::Validate(page, page_size, validation, &view, error);
        if (status != OPENUSD_PHYSX_STATUS_OK)
        {
            world->state = OPENUSD_PHYSX_WORLD_STATE_FAULTED;
            return status;
        }
        return BuildContent(*world, view, error);
    });
}

openusd_physx_status openusd_physx_world_reset(
    openusd_physx_world* world,
    const openusd_physx_reset_desc* desc,
    openusd_physx_error_buffer* error)
{
    return Guard(error, [&]() -> openusd_physx_status
    {
        if (world == nullptr)
        {
            WriteError(error, "The world pointer must not be null.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        const std::lock_guard<std::mutex> lock(world->mutex);
        return ResetWorld(*world, desc, error);
    });
}

openusd_physx_status openusd_physx_world_step(
    openusd_physx_world* world,
    const openusd_physx_step_desc* desc,
    openusd_physx_result_page* results,
    openusd_physx_error_buffer* error)
{
    return Guard(error, [&]() -> openusd_physx_status
    {
        if (world == nullptr || desc == nullptr)
        {
            WriteError(error, "The world and step description pointers must not be null.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        if (desc->struct_size != sizeof(openusd_physx_step_desc))
        {
            WriteError(error, "The step description structure size does not match this ABI.");
            return OPENUSD_PHYSX_STATUS_VERSION_MISMATCH;
        }
        const std::lock_guard<std::mutex> lock(world->mutex);
        openusd_physx_status status = ValidateResultPage(*world, results, error);
        if (status != OPENUSD_PHYSX_STATUS_OK)
        {
            return status;
        }
        status = StepWorld(*world, *desc, error);
        if (status != OPENUSD_PHYSX_STATUS_OK)
        {
            return status;
        }
        FillResults(*world, *results);
        return OPENUSD_PHYSX_STATUS_OK;
    });
}

openusd_physx_status openusd_physx_world_fetch_results(
    openusd_physx_world* world,
    openusd_physx_result_page* results,
    openusd_physx_error_buffer* error)
{
    return Guard(error, [&]() -> openusd_physx_status
    {
        if (world == nullptr)
        {
            WriteError(error, "The world pointer must not be null.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        const std::lock_guard<std::mutex> lock(world->mutex);
        const openusd_physx_status status = ValidateResultPage(*world, results, error);
        if (status != OPENUSD_PHYSX_STATUS_OK)
        {
            return status;
        }
        FillResults(*world, *results);
        return OPENUSD_PHYSX_STATUS_OK;
    });
}

openusd_physx_status openusd_physx_world_query(
    openusd_physx_world* world,
    const openusd_physx_query_desc* desc,
    openusd_physx_query_result* result,
    openusd_physx_error_buffer* error)
{
    return Guard(error, [&]() -> openusd_physx_status
    {
        if (world == nullptr || desc == nullptr || result == nullptr)
        {
            WriteError(error, "The world, query description, and query result pointers must not be null.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        if (desc->struct_size != sizeof(openusd_physx_query_desc) ||
            result->struct_size != sizeof(openusd_physx_query_result) ||
            desc->abi_version != OPENUSD_PHYSX_WORLD_ABI_VERSION)
        {
            WriteError(error, "The query ABI version or structure size does not match this ABI exactly.");
            return OPENUSD_PHYSX_STATUS_VERSION_MISMATCH;
        }
        result->overflow_flags = 0;
        result->hit_count = 0;
        result->dropped_hit_count = 0;
        result->rejected_request_count = 0;
        const std::lock_guard<std::mutex> lock(world->mutex);
        return RunQueries(*world, *desc, *result, error);
    });
}

openusd_physx_status openusd_physx_world_get_status(
    const openusd_physx_world* world,
    openusd_physx_world_status_info* info,
    openusd_physx_error_buffer* error)
{
    return Guard(error, [&]() -> openusd_physx_status
    {
        if (world == nullptr || info == nullptr)
        {
            WriteError(error, "The world and status pointers must not be null.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        if (info->struct_size != sizeof(openusd_physx_world_status_info))
        {
            WriteError(error, "The status structure size does not match this ABI.");
            return OPENUSD_PHYSX_STATUS_VERSION_MISMATCH;
        }
        const std::lock_guard<std::mutex> lock(world->mutex);
        info->state = world->state;
        info->revision = world->revision;
        info->step_index = world->step_index;
        info->simulation_time = world->simulation_time;
        info->actor_count = static_cast<uint32_t>(world->actors.size());
        info->dynamic_actor_count = world->dynamic_actor_count;
        info->joint_count = static_cast<uint32_t>(world->joints.size());
        info->scene_count = static_cast<uint32_t>(world->scenes.size());
        info->articulation_count = static_cast<uint32_t>(world->articulations.size());
        info->articulation_link_count = static_cast<uint32_t>(world->articulation_links.size());
        info->controller_count = static_cast<uint32_t>(world->controllers.size());
        info->tendon_count = world->tendon_count;
        info->mimic_joint_count = world->mimic_joint_count;
        info->vehicle_count = static_cast<uint32_t>(world->vehicles.size());
        info->vehicle_wheel_count = world->vehicle_wheel_count;
        const openusd_physx_gpu::Counts& gpu = world->gpu.GetCounts();
        info->particle_system_count = gpu.particle_systems;
        info->particle_body_count = gpu.particle_bodies;
        info->deformable_surface_count = gpu.surfaces;
        info->deformable_volume_count = gpu.volumes;
        info->deformation_body_count = gpu.deformation_bodies;
        info->deformation_point_count = gpu.deformation_points;
        info->reserved0 = 0;
        info->capacities = world->capacities;
        // The declared deformation capacities always cover what this world
        // actually publishes. A page may legally declare more GPU objects than
        // could be created, and a caller that sized its buffers from the page
        // would then over allocate; reporting the built counts instead lets a
        // caller allocate exactly what it will receive.
        info->capacities.max_deformation_bodies =
            gpu.deformation_bodies > world->capacities.max_deformation_bodies
            ? gpu.deformation_bodies
            : world->capacities.max_deformation_bodies;
        info->capacities.max_deformation_points =
            gpu.deformation_points > world->capacities.max_deformation_points
            ? gpu.deformation_points
            : world->capacities.max_deformation_points;
        return OPENUSD_PHYSX_STATUS_OK;
    });
}
