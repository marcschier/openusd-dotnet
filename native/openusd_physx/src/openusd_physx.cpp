// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_physx.h"

#include <PxPhysicsAPI.h>

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <exception>
#include <memory>
#include <string>
#include <string_view>
#include <vector>

namespace
{
using namespace physx;

class ErrorCallback final : public PxErrorCallback
{
public:
    void reportError(PxErrorCode::Enum code, const char* message, const char* file, int line) override
    {
        static_cast<void>(code);
        static_cast<void>(file);
        static_cast<void>(line);
        last_message = message == nullptr ? std::string() : std::string(message);
    }

    std::string last_message;
};

PxDefaultAllocator g_allocator;
ErrorCallback g_error_callback;

void WriteError(openusd_physx_error_buffer* error, std::string_view message) noexcept
{
    if (error == nullptr)
    {
        return;
    }
    error->required = message.size() + 1;
    if (error->data == nullptr || error->capacity == 0)
    {
        return;
    }
    const size_t count = std::min(message.size(), error->capacity - 1);
    std::memcpy(error->data, message.data(), count);
    error->data[count] = '\0';
}

void ResetError(openusd_physx_error_buffer* error) noexcept
{
    if (error == nullptr)
    {
        return;
    }
    error->required = 0;
    if (error->data != nullptr && error->capacity != 0)
    {
        error->data[0] = '\0';
    }
}

template <typename TAction>
openusd_physx_status Guard(openusd_physx_error_buffer* error, TAction&& action) noexcept
{
    try
    {
        ResetError(error);
        g_error_callback.last_message.clear();
        return action();
    }
    catch (const std::exception& exception)
    {
        WriteError(error, exception.what());
        return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
    }
    catch (...)
    {
        WriteError(error, "Unknown PhysX exception.");
        return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
    }
}

openusd_physx_status CopyString(
    const std::string& value,
    char* buffer,
    size_t capacity,
    size_t* required) noexcept
{
    if (required == nullptr)
    {
        return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
    }
    *required = value.size() + 1;
    if (buffer == nullptr || capacity < *required)
    {
        return OPENUSD_PHYSX_STATUS_BUFFER_TOO_SMALL;
    }
    std::memcpy(buffer, value.c_str(), *required);
    return OPENUSD_PHYSX_STATUS_OK;
}

PxVec3 ToPx(openusd_physx_vec3f value) noexcept
{
    return PxVec3(value.x, value.y, value.z);
}

PxQuat ToPx(openusd_physx_quatf value) noexcept
{
    return PxQuat(value.x, value.y, value.z, value.w);
}

bool IsFinite(openusd_physx_vec3f value) noexcept
{
    return std::isfinite(value.x) && std::isfinite(value.y) && std::isfinite(value.z);
}

bool IsFinite(openusd_physx_quatf value) noexcept
{
    return std::isfinite(value.x) && std::isfinite(value.y) &&
        std::isfinite(value.z) && std::isfinite(value.w);
}

bool IsValidMaterial(float static_friction, float dynamic_friction, float restitution) noexcept
{
    return std::isfinite(static_friction) && static_friction >= 0.0F &&
        std::isfinite(dynamic_friction) && dynamic_friction >= 0.0F &&
        std::isfinite(restitution) && restitution >= 0.0F && restitution <= 1.0F;
}
}

struct openusd_physx_scene
{
    PxFoundation* foundation = nullptr;
    PxPhysics* physics = nullptr;
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
        result->foundation = PxCreateFoundation(PX_PHYSICS_VERSION, g_allocator, g_error_callback);
        if (result->foundation == nullptr)
        {
            WriteError(error, "PxCreateFoundation failed.");
            return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
        }
        result->physics = PxCreatePhysics(PX_PHYSICS_VERSION, *result->foundation, PxTolerancesScale());
        if (result->physics == nullptr)
        {
            WriteError(error, "PxCreatePhysics failed.");
            return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
        }
        PxSceneDesc scene_desc(result->physics->getTolerancesScale());
        scene_desc.gravity = ToPx(gravity);
        result->dispatcher = PxDefaultCpuDispatcherCreate(2);
        scene_desc.cpuDispatcher = result->dispatcher;
        scene_desc.filterShader = PxDefaultSimulationFilterShader;
        result->scene = result->physics->createScene(scene_desc);
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
    if (scene->scene != nullptr)
    {
        scene->scene->release();
    }
    if (scene->dispatcher != nullptr)
    {
        scene->dispatcher->release();
    }
    if (scene->physics != nullptr)
    {
        scene->physics->release();
    }
    if (scene->foundation != nullptr)
    {
        scene->foundation->release();
    }
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