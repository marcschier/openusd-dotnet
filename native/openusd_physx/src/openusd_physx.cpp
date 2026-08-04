// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_physx.h"

#include <PxPhysicsAPI.h>

#include <pxr/base/gf/matrix4d.h>
#include <pxr/base/gf/quatd.h>
#include <pxr/base/gf/rotation.h>
#include <pxr/base/tf/errorMark.h>
#include <pxr/usd/usd/primRange.h>
#include <pxr/usd/usd/stage.h>
#include <pxr/usd/usdGeom/capsule.h>
#include <pxr/usd/usdGeom/cube.h>
#include <pxr/usd/usdGeom/plane.h>
#include <pxr/usd/usdGeom/sphere.h>
#include <pxr/usd/usdGeom/xformable.h>
#include <pxr/usd/usdPhysics/collisionAPI.h>
#include <pxr/usd/usdPhysics/massAPI.h>
#include <pxr/usd/usdPhysics/materialAPI.h>
#include <pxr/usd/usdPhysics/rigidBodyAPI.h>
#include <pxr/usd/usdPhysics/scene.h>

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <exception>
#include <memory>
#include <mutex>
#include <string>
#include <string_view>
#include <vector>

namespace
{
using namespace physx;
PXR_NAMESPACE_USING_DIRECTIVE

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
std::mutex g_physx_mutex;

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

struct StageBodyBinding
{
    UsdPrim prim;
    PxRigidDynamic* actor = nullptr;
};

struct StageMaterial
{
    float static_friction = 0.5F;
    float dynamic_friction = 0.5F;
    float restitution = 0.0F;
    float density = 1.0F;
};

GfMatrix4d PoseToMatrix(const PxTransform& pose)
{
    GfMatrix4d matrix(1.0);
    const GfQuatd rotation(pose.q.w, pose.q.x, pose.q.y, pose.q.z);
    matrix.SetRotate(GfRotation(rotation));
    matrix.SetTranslateOnly(GfVec3d(pose.p.x, pose.p.y, pose.p.z));
    return matrix;
}

PxTransform MatrixToPose(const GfMatrix4d& matrix)
{
    const GfVec3d translation = matrix.ExtractTranslation();
    const GfQuatd rotation = matrix.ExtractRotationQuat();
    return PxTransform(
        PxVec3(
            static_cast<float>(translation[0]),
            static_cast<float>(translation[1]),
            static_cast<float>(translation[2])),
        PxQuat(
            static_cast<float>(rotation.GetImaginary()[0]),
            static_cast<float>(rotation.GetImaginary()[1]),
            static_cast<float>(rotation.GetImaginary()[2]),
            static_cast<float>(rotation.GetReal())).getNormalized());
}

bool GetBool(const UsdAttribute& attribute, bool fallback)
{
    bool value = fallback;
    return attribute && attribute.Get(&value) ? value : fallback;
}

float GetFloat(const UsdAttribute& attribute, float fallback)
{
    float value = fallback;
    return attribute && attribute.Get(&value) ? value : fallback;
}

double GetDouble(const UsdAttribute& attribute, double fallback)
{
    double value = fallback;
    return attribute && attribute.Get(&value) ? value : fallback;
}

GfVec3f GetVec3f(const UsdAttribute& attribute, GfVec3f fallback)
{
    GfVec3f value = fallback;
    return attribute && attribute.Get(&value) ? value : fallback;
}

StageMaterial ReadMaterial(const UsdPrim& prim)
{
    StageMaterial material;
    const UsdPhysicsMaterialAPI material_api(prim);
    if (material_api)
    {
        material.static_friction = GetFloat(material_api.GetStaticFrictionAttr(), material.static_friction);
        material.dynamic_friction = GetFloat(material_api.GetDynamicFrictionAttr(), material.dynamic_friction);
        material.restitution = GetFloat(material_api.GetRestitutionAttr(), material.restitution);
        material.density = GetFloat(material_api.GetDensityAttr(), material.density);
    }
    const UsdPhysicsMassAPI mass_api(prim);
    if (mass_api)
    {
        material.density = GetFloat(mass_api.GetDensityAttr(), material.density);
    }
    material.static_friction = std::max(0.0F, material.static_friction);
    material.dynamic_friction = std::max(0.0F, material.dynamic_friction);
    material.restitution = std::clamp(material.restitution, 0.0F, 1.0F);
    material.density = std::max(0.0001F, material.density);
    return material;
}

PxRigidActor* CreateActorForCollider(
    PxPhysics& physics,
    const UsdPrim& prim,
    const PxTransform& pose,
    const StageMaterial& material,
    bool dynamic)
{
    PxMaterial* px_material = physics.createMaterial(
        material.static_friction,
        material.dynamic_friction,
        material.restitution);
    if (px_material == nullptr)
    {
        return nullptr;
    }

    PxGeometryHolder geometry;
    if (prim.IsA<UsdGeomCube>())
    {
        const UsdGeomCube cube(prim);
        const float half_size = static_cast<float>(GetDouble(cube.GetSizeAttr(), 2.0) * 0.5);
        geometry.storeAny(PxBoxGeometry(half_size, half_size, half_size));
    }
    else if (prim.IsA<UsdGeomSphere>())
    {
        const UsdGeomSphere sphere(prim);
        const float radius = static_cast<float>(GetDouble(sphere.GetRadiusAttr(), 1.0));
        geometry.storeAny(PxSphereGeometry(radius));
    }
    else if (prim.IsA<UsdGeomCapsule>())
    {
        const UsdGeomCapsule capsule(prim);
        const float radius = static_cast<float>(GetDouble(capsule.GetRadiusAttr(), 1.0));
        const float half_height = static_cast<float>(GetDouble(capsule.GetHeightAttr(), 2.0) * 0.5);
        geometry.storeAny(PxCapsuleGeometry(radius, half_height));
    }
    else
    {
        px_material->release();
        return nullptr;
    }

    PxRigidActor* actor = dynamic
        ? static_cast<PxRigidActor*>(PxCreateDynamic(physics, pose, geometry.any(), *px_material, material.density))
        : static_cast<PxRigidActor*>(PxCreateStatic(physics, pose, geometry.any(), *px_material));
    px_material->release();
    return actor;
}

openusd_physx_status SimulateStage(
    const char* stage_path,
    float time_step,
    uint32_t step_count,
    openusd_physx_error_buffer* error)
{
    std::lock_guard<std::mutex> lock(g_physx_mutex);
    if (stage_path == nullptr || stage_path[0] == '\0' || !std::isfinite(time_step) || time_step <= 0.0F)
    {
        WriteError(error, "A stage path and positive finite time step are required.");
        return OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT;
    }

    UsdStageRefPtr stage = UsdStage::Open(stage_path);
    if (!stage)
    {
        WriteError(error, std::string("Could not open USD stage: ") + stage_path);
        return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
    }

    GfVec3f gravity_direction(0.0F, -1.0F, 0.0F);
    float gravity_magnitude = 9.81F;
    for (const UsdPrim& prim : stage->Traverse())
    {
        if (prim.IsA<UsdPhysicsScene>())
        {
            const UsdPhysicsScene scene_schema(prim);
            gravity_direction = GetVec3f(scene_schema.GetGravityDirectionAttr(), gravity_direction);
            gravity_magnitude = GetFloat(scene_schema.GetGravityMagnitudeAttr(), gravity_magnitude);
            break;
        }
    }

    PxFoundation* foundation = PxCreateFoundation(PX_PHYSICS_VERSION, g_allocator, g_error_callback);
    if (foundation == nullptr)
    {
        WriteError(error, "PxCreateFoundation failed.");
        return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
    }
    PxPhysics* physics = PxCreatePhysics(PX_PHYSICS_VERSION, *foundation, PxTolerancesScale());
    if (physics == nullptr)
    {
        foundation->release();
        WriteError(error, "PxCreatePhysics failed.");
        return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
    }
    PxDefaultCpuDispatcher* dispatcher = PxDefaultCpuDispatcherCreate(2);
    PxSceneDesc scene_desc(physics->getTolerancesScale());
    scene_desc.gravity = PxVec3(
        gravity_direction[0] * gravity_magnitude,
        gravity_direction[1] * gravity_magnitude,
        gravity_direction[2] * gravity_magnitude);
    scene_desc.cpuDispatcher = dispatcher;
    scene_desc.filterShader = PxDefaultSimulationFilterShader;
    PxScene* scene = physics->createScene(scene_desc);
    if (scene == nullptr)
    {
        dispatcher->release();
        physics->release();
        foundation->release();
        WriteError(error, "PxPhysics::createScene failed.");
        return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
    }

    std::vector<StageBodyBinding> bodies;
    for (const UsdPrim& prim : stage->Traverse())
    {
        const UsdPhysicsCollisionAPI collision_api(prim);
        if (!collision_api || !GetBool(collision_api.GetCollisionEnabledAttr(), true))
        {
            continue;
        }

        const UsdPhysicsRigidBodyAPI rigid_body(prim);
        const bool dynamic = rigid_body && GetBool(rigid_body.GetRigidBodyEnabledAttr(), true) &&
            !GetBool(rigid_body.GetKinematicEnabledAttr(), false);
        const StageMaterial material = ReadMaterial(prim);
        const UsdGeomXformable xformable(prim);
        const GfMatrix4d matrix = xformable
            ? xformable.ComputeLocalToWorldTransform(UsdTimeCode::Default())
            : GfMatrix4d(1.0);
        const PxTransform pose = MatrixToPose(matrix);

        PxRigidActor* actor = nullptr;
        if (!dynamic && prim.IsA<UsdGeomPlane>())
        {
            PxMaterial* px_material = physics->createMaterial(
                material.static_friction,
                material.dynamic_friction,
                material.restitution);
            actor = PxCreatePlane(*physics, PxPlane(0.0F, 1.0F, 0.0F, -pose.p.y), *px_material);
            px_material->release();
        }
        else
        {
            actor = CreateActorForCollider(*physics, prim, pose, material, dynamic);
        }
        if (actor == nullptr)
        {
            continue;
        }

        if (dynamic)
        {
            PxRigidDynamic* body = static_cast<PxRigidDynamic*>(actor);
            const GfVec3f velocity = GetVec3f(rigid_body.GetVelocityAttr(), GfVec3f(0.0F));
            const GfVec3f angular_velocity = GetVec3f(rigid_body.GetAngularVelocityAttr(), GfVec3f(0.0F));
            body->setLinearVelocity(PxVec3(velocity[0], velocity[1], velocity[2]));
            body->setAngularVelocity(PxVec3(angular_velocity[0], angular_velocity[1], angular_velocity[2]));
            bodies.push_back({prim, body});
        }
        scene->addActor(*actor);
    }

    for (uint32_t step = 0; step < step_count; ++step)
    {
        scene->simulate(time_step);
        scene->fetchResults(true);
    }

    TfErrorMark mark;
    for (const StageBodyBinding& binding : bodies)
    {
        UsdGeomXformable xformable(binding.prim);
        const UsdGeomXformOp operation = xformable.MakeMatrixXform();
        if (!operation || !operation.Set(PoseToMatrix(binding.actor->getGlobalPose())))
        {
            WriteError(error, "Could not write a simulated transform back to the stage.");
            scene->release();
            dispatcher->release();
            physics->release();
            foundation->release();
            return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
        }
    }
    if (!mark.IsClean())
    {
        WriteError(error, "OpenUSD reported an error while writing simulated transforms.");
        scene->release();
        dispatcher->release();
        physics->release();
        foundation->release();
        return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
    }
    if (!stage->GetRootLayer()->Save())
    {
        WriteError(error, "Could not save simulated stage transforms.");
        scene->release();
        dispatcher->release();
        physics->release();
        foundation->release();
        return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
    }

    scene->release();
    dispatcher->release();
    physics->release();
    foundation->release();
    return OPENUSD_PHYSX_STATUS_OK;
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
        std::lock_guard<std::mutex> lock(g_physx_mutex);
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

openusd_physx_status openusd_physx_stage_simulate_file(
    const char* stage_path,
    float time_step,
    uint32_t step_count,
    openusd_physx_error_buffer* error)
{
    return Guard(error, [&]() -> openusd_physx_status
    {
        return SimulateStage(stage_path, time_step, step_count, error);
    });
}