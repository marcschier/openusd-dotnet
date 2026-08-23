// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_physx.h"
#include "openusd_physx_runtime.h"
#include "openusd_physx_support.h"
#include "openusd_physx_translate.h"

#include <PxPhysicsAPI.h>

#include <cmath>
#include <cstdio>
#include <memory>
#include <string>
#include <vector>

namespace
{
using namespace physx;

using openusd_physx_support::CopyString;
using openusd_physx_support::Guard;
using openusd_physx_support::IsFinite;
using openusd_physx_support::IsValidMaterial;
using openusd_physx_support::WriteError;
using openusd_physx_translate::ToPx;
} // namespace

struct openusd_physx_scene
{
    openusd_physx_runtime::Reference runtime;
    PxPhysics* physics = nullptr; // owned by the process runtime, never released here
    PxDefaultCpuDispatcher* dispatcher = nullptr;
    PxScene* scene = nullptr;
    std::vector<PxRigidDynamic*> dynamics;
};

openusd_physx_status openusd_physx_get_version(
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_physx_error_buffer* error)
{
    return Guard(error, [&]() -> openusd_physx_status
    {
        char version[64]{};
        std::snprintf(
            version,
            sizeof(version),
            "%u.%u.%u",
            PX_PHYSICS_VERSION_MAJOR,
            PX_PHYSICS_VERSION_MINOR,
            PX_PHYSICS_VERSION_BUGFIX);
        return CopyString(version, buffer, capacity, required);
    });
}

openusd_physx_status openusd_physx_scene_create(
    openusd_physx_vec3f gravity,
    openusd_physx_scene** scene,
    openusd_physx_error_buffer* error)
{
    return Guard(error, [&]() -> openusd_physx_status
    {
        if (scene == nullptr || !IsFinite(gravity))
        {
            WriteError(error, "A scene output and finite gravity vector are required.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        *scene = nullptr;
        auto result = std::make_unique<openusd_physx_scene>();
        std::string reason;
        if (!result->runtime.Acquire(reason))
        {
            WriteError(error, reason);
            return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
        }
        result->physics = &openusd_physx_runtime::Physics();
        PxSceneDesc scene_desc(result->physics->getTolerancesScale());
        scene_desc.gravity = ToPx(gravity);
        result->dispatcher = PxDefaultCpuDispatcherCreate(2);
        if (result->dispatcher == nullptr)
        {
            WriteError(error, "PxDefaultCpuDispatcherCreate failed.");
            return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
        }
        scene_desc.cpuDispatcher = result->dispatcher;
        scene_desc.filterShader = PxDefaultSimulationFilterShader;
        {
            // The factory lock is scoped to the shared factory call only. It
            // must not be held when result is destroyed on a failure path,
            // because destroying the runtime reference takes the runtime
            // lifetime lock, which the documented ordering forbids underneath
            // the factory lock.
            const openusd_physx_runtime::FactoryLock factory_lock;
            result->scene = result->physics->createScene(scene_desc);
        }
        if (result->scene == nullptr)
        {
            WriteError(error, "PxPhysics::createScene failed.");
            return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
        }
        *scene = result.release();
        return OPENUSD_PHYSX_STATUS_OK;
    });
}

void openusd_physx_scene_release(openusd_physx_scene* scene)
{
    if (scene == nullptr)
    {
        return;
    }
    {
        // Only the PxScene comes from the shared factory, so the factory lock
        // is scoped to that release alone. It must be dropped before the
        // runtime reference is released, because releasing the last reference
        // takes the runtime lifetime lock and the documented ordering forbids
        // taking that lock while the factory lock is held.
        const openusd_physx_runtime::FactoryLock factory_lock;
        if (scene->scene != nullptr)
        {
            scene->scene->release();
            scene->scene = nullptr;
        }
    }
    if (scene->dispatcher != nullptr)
    {
        // The CPU dispatcher is owned by this scene, not by the shared factory.
        scene->dispatcher->release();
        scene->dispatcher = nullptr;
    }
    scene->physics = nullptr;
    // Destroys the runtime reference, which may take the runtime lifetime lock.
    delete scene;
}

openusd_physx_status openusd_physx_scene_add_static_plane(
    openusd_physx_scene* scene,
    float y,
    float static_friction,
    float dynamic_friction,
    float restitution,
    openusd_physx_error_buffer* error)
{
    return Guard(error, [&]() -> openusd_physx_status
    {
        if (scene == nullptr || scene->physics == nullptr || scene->scene == nullptr || !std::isfinite(y) ||
            !IsValidMaterial(static_friction, dynamic_friction, restitution))
        {
            WriteError(error, "A valid scene, plane height, and material are required.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        const openusd_physx_runtime::FactoryLock factory_lock;
        PxMaterial* material = scene->physics->createMaterial(static_friction, dynamic_friction, restitution);
        PxRigidStatic* plane = PxCreatePlane(*scene->physics, PxPlane(0.0F, 1.0F, 0.0F, -y), *material);
        material->release();
        if (plane == nullptr)
        {
            WriteError(error, "PxCreatePlane failed.");
            return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
        }
        scene->scene->addActor(*plane);
        return OPENUSD_PHYSX_STATUS_OK;
    });
}

openusd_physx_status openusd_physx_scene_add_dynamic_box(
    openusd_physx_scene* scene,
    openusd_physx_vec3f position,
    openusd_physx_quatf rotation,
    openusd_physx_vec3f half_extents,
    openusd_physx_vec3f linear_velocity,
    openusd_physx_vec3f angular_velocity,
    float density,
    float static_friction,
    float dynamic_friction,
    float restitution,
    openusd_physx_error_buffer* error)
{
    return Guard(error, [&]() -> openusd_physx_status
    {
        if (scene == nullptr || scene->physics == nullptr || scene->scene == nullptr || !IsFinite(position) ||
            !IsFinite(rotation) || !IsFinite(half_extents) || !IsFinite(linear_velocity) ||
            !IsFinite(angular_velocity) || half_extents.x <= 0.0F || half_extents.y <= 0.0F ||
            half_extents.z <= 0.0F || !std::isfinite(density) || density <= 0.0F ||
            !IsValidMaterial(static_friction, dynamic_friction, restitution))
        {
            WriteError(error, "A valid scene, box, velocities, density, and material are required.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        const openusd_physx_runtime::FactoryLock factory_lock;
        PxMaterial* material = scene->physics->createMaterial(static_friction, dynamic_friction, restitution);
        const PxTransform transform(ToPx(position), ToPx(rotation).getNormalized());
        PxRigidDynamic* body = PxCreateDynamic(
            *scene->physics,
            transform,
            PxBoxGeometry(ToPx(half_extents)),
            *material,
            density);
        material->release();
        if (body == nullptr)
        {
            WriteError(error, "PxCreateDynamic failed.");
            return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
        }
        body->setLinearVelocity(ToPx(linear_velocity));
        body->setAngularVelocity(ToPx(angular_velocity));
        scene->scene->addActor(*body);
        scene->dynamics.push_back(body);
        return OPENUSD_PHYSX_STATUS_OK;
    });
}

openusd_physx_status openusd_physx_scene_step(
    openusd_physx_scene* scene,
    float time_step,
    uint32_t step_count,
    openusd_physx_error_buffer* error)
{
    return Guard(error, [&]() -> openusd_physx_status
    {
        if (scene == nullptr || scene->scene == nullptr || !std::isfinite(time_step) || time_step <= 0.0F)
        {
            WriteError(error, "A valid scene and positive finite time step are required.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        for (uint32_t step = 0; step < step_count; ++step)
        {
            scene->scene->simulate(time_step);
            scene->scene->fetchResults(true);
        }
        return OPENUSD_PHYSX_STATUS_OK;
    });
}

openusd_physx_status openusd_physx_scene_get_dynamic_transforms(
    const openusd_physx_scene* scene,
    openusd_physx_transform* transforms,
    size_t capacity,
    size_t* count,
    openusd_physx_error_buffer* error)
{
    return Guard(error, [&]() -> openusd_physx_status
    {
        if (scene == nullptr || count == nullptr || (capacity > 0 && transforms == nullptr))
        {
            WriteError(error, "A valid scene and transform output are required.");
            return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
        }
        *count = scene->dynamics.size();
        if (capacity < scene->dynamics.size())
        {
            return OPENUSD_PHYSX_STATUS_BUFFER_TOO_SMALL;
        }
        for (size_t index = 0; index < scene->dynamics.size(); ++index)
        {
            const PxTransform pose = scene->dynamics[index]->getGlobalPose();
            transforms[index].position = {pose.p.x, pose.p.y, pose.p.z};
            transforms[index].rotation = {pose.q.x, pose.q.y, pose.q.z, pose.q.w};
        }
        return OPENUSD_PHYSX_STATUS_OK;
    });
}
