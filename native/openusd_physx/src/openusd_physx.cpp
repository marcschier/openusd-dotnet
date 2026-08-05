// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_physx.h"

#include <PxPhysicsAPI.h>
#include <cooking/PxCooking.h>

#include <pxr/base/gf/matrix4d.h>
#include <pxr/base/gf/quatd.h>
#include <pxr/base/gf/rotation.h>
#include <pxr/base/tf/errorMark.h>
#include <pxr/base/tf/token.h>
#include <pxr/base/vt/array.h>
#include <pxr/usd/usd/primRange.h>
#include <pxr/usd/usd/relationship.h>
#include <pxr/usd/usd/stage.h>
#include <pxr/usd/usdGeom/capsule.h>
#include <pxr/usd/usdGeom/cube.h>
#include <pxr/usd/usdGeom/mesh.h>
#include <pxr/usd/usdGeom/plane.h>
#include <pxr/usd/usdGeom/sphere.h>
#include <pxr/usd/usdGeom/xformable.h>
#include <pxr/usd/usdPhysics/collisionAPI.h>
#include <pxr/usd/usdPhysics/collisionGroup.h>
#include <pxr/usd/usdPhysics/distanceJoint.h>
#include <pxr/usd/usdPhysics/driveAPI.h>
#include <pxr/usd/usdPhysics/filteredPairsAPI.h>
#include <pxr/usd/usdPhysics/fixedJoint.h>
#include <pxr/usd/usdPhysics/limitAPI.h>
#include <pxr/usd/usdPhysics/massAPI.h>
#include <pxr/usd/usdPhysics/materialAPI.h>
#include <pxr/usd/usdPhysics/meshCollisionAPI.h>
#include <pxr/usd/usdPhysics/rigidBodyAPI.h>
#include <pxr/usd/usdPhysics/prismaticJoint.h>
#include <pxr/usd/usdPhysics/revoluteJoint.h>
#include <pxr/usd/usdPhysics/scene.h>
#include <pxr/usd/usdPhysics/sphericalJoint.h>

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <exception>
#include <memory>
#include <mutex>
#include <unordered_map>
#include <unordered_set>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace
{
using namespace physx;
PXR_NAMESPACE_USING_DIRECTIVE

PxFilterFlags StageFilterShader(
    PxFilterObjectAttributes attributes0,
    PxFilterData filter_data0,
    PxFilterObjectAttributes attributes1,
    PxFilterData filter_data1,
    PxPairFlags& pair_flags,
    const void* constant_block,
    PxU32 constant_block_size)
{
    static_cast<void>(attributes0);
    static_cast<void>(attributes1);
    static_cast<void>(constant_block);
    static_cast<void>(constant_block_size);
    if (((filter_data0.word0 & filter_data1.word1) == 0) ||
        ((filter_data1.word0 & filter_data0.word1) == 0))
    {
        return PxFilterFlag::eSUPPRESS;
    }
    pair_flags = PxPairFlag::eCONTACT_DEFAULT;
    return PxFilterFlags();
}

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

struct StageActorBinding
{
    UsdPrim prim;
    PxRigidActor* actor = nullptr;
    uint32_t bit = 0;
    uint32_t mask = 0xFFFFFFFFU;
};

struct StageMaterial
{
    float static_friction = 0.5F;
    float dynamic_friction = 0.5F;
    float restitution = 0.0F;
    float density = 1.0F;
};

struct StageResources
{
    std::vector<PxConvexMesh*> convex_meshes;
    std::vector<PxTriangleMesh*> triangle_meshes;
    std::vector<PxJoint*> joints;

    void Release() noexcept
    {
        for (PxJoint* joint : joints)
        {
            joint->release();
        }
        for (PxTriangleMesh* mesh : triangle_meshes)
        {
            mesh->release();
        }
        for (PxConvexMesh* mesh : convex_meshes)
        {
            mesh->release();
        }
    }
};

struct PairHash
{
    size_t operator()(const std::pair<std::string, std::string>& value) const noexcept
    {
        return std::hash<std::string>{}(value.first) ^ (std::hash<std::string>{}(value.second) << 1);
    }
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

GfQuatf GetQuatf(const UsdAttribute& attribute, GfQuatf fallback)
{
    GfQuatf value = fallback;
    return attribute && attribute.Get(&value) ? value : fallback;
}

TfToken GetToken(const UsdAttribute& attribute, const TfToken& fallback)
{
    TfToken value = fallback;
    return attribute && attribute.Get(&value) ? value : fallback;
}

PxTransform JointFrame(const GfVec3f& position, const GfQuatf& rotation)
{
    const GfVec3f imaginary = rotation.GetImaginary();
    return PxTransform(
        PxVec3(position[0], position[1], position[2]),
        PxQuat(imaginary[0], imaginary[1], imaginary[2], rotation.GetReal()).getNormalized());
}

PxTransform AxisFrame(const PxTransform& frame, const TfToken& axis)
{
    PxQuat axis_rotation(PxIdentity);
    if (axis == TfToken("Y"))
    {
        axis_rotation = PxQuat(PxHalfPi, PxVec3(0.0F, 0.0F, 1.0F));
    }
    else if (axis == TfToken("Z"))
    {
        axis_rotation = PxQuat(-PxHalfPi, PxVec3(0.0F, 1.0F, 0.0F));
    }
    return PxTransform(frame.p, (frame.q * axis_rotation).getNormalized());
}

std::string PrimPath(const UsdPrim& prim)
{
    return prim.GetPath().GetString();
}

std::pair<std::string, std::string> OrderedPair(std::string first, std::string second)
{
    if (second < first)
    {
        std::swap(first, second);
    }
    return {std::move(first), std::move(second)};
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

bool ReadMeshTriangles(
    const UsdGeomMesh& mesh,
    std::vector<PxVec3>& vertices,
    std::vector<uint32_t>& triangles)
{
    VtArray<GfVec3f> points;
    VtArray<int> face_counts;
    VtArray<int> face_indices;
    if (!mesh.GetPointsAttr().Get(&points) ||
        !mesh.GetFaceVertexCountsAttr().Get(&face_counts) ||
        !mesh.GetFaceVertexIndicesAttr().Get(&face_indices))
    {
        return false;
    }

    vertices.reserve(points.size());
    for (const GfVec3f& point : points)
    {
        vertices.emplace_back(point[0], point[1], point[2]);
    }

    size_t offset = 0;
    for (int count : face_counts)
    {
        if (count < 3 || offset + static_cast<size_t>(count) > face_indices.size())
        {
            return false;
        }
        const int first = face_indices[offset];
        for (int index = 1; index < count - 1; ++index)
        {
            const int second = face_indices[offset + static_cast<size_t>(index)];
            const int third = face_indices[offset + static_cast<size_t>(index) + 1];
            if (first < 0 || second < 0 || third < 0 ||
                first >= static_cast<int>(vertices.size()) ||
                second >= static_cast<int>(vertices.size()) ||
                third >= static_cast<int>(vertices.size()))
            {
                return false;
            }
            triangles.push_back(static_cast<uint32_t>(first));
            triangles.push_back(static_cast<uint32_t>(second));
            triangles.push_back(static_cast<uint32_t>(third));
        }
        offset += static_cast<size_t>(count);
    }
    return offset == face_indices.size() && !vertices.empty() && !triangles.empty();
}

PxConvexMesh* CookConvexMesh(
    PxPhysics& physics,
    const PxCookingParams& cooking_params,
    const std::vector<PxVec3>& vertices)
{
    PxConvexMeshDesc desc;
    desc.points.count = static_cast<PxU32>(vertices.size());
    desc.points.stride = sizeof(PxVec3);
    desc.points.data = vertices.data();
    desc.flags = PxConvexFlag::eCOMPUTE_CONVEX;
    return PxCreateConvexMesh(cooking_params, desc, physics.getPhysicsInsertionCallback());
}

PxTriangleMesh* CookTriangleMesh(
    PxPhysics& physics,
    const PxCookingParams& cooking_params,
    const std::vector<PxVec3>& vertices,
    const std::vector<uint32_t>& triangles)
{
    PxTriangleMeshDesc desc;
    desc.points.count = static_cast<PxU32>(vertices.size());
    desc.points.stride = sizeof(PxVec3);
    desc.points.data = vertices.data();
    desc.triangles.count = static_cast<PxU32>(triangles.size() / 3);
    desc.triangles.stride = 3 * sizeof(uint32_t);
    desc.triangles.data = triangles.data();
    return PxCreateTriangleMesh(cooking_params, desc, physics.getPhysicsInsertionCallback());
}

void ReleaseStageSimulation(
    PxScene* scene,
    PxDefaultCpuDispatcher* dispatcher,
    PxPhysics* physics,
    PxFoundation* foundation,
    StageResources& resources) noexcept
{
    resources.Release();
    if (scene != nullptr)
    {
        scene->release();
    }
    if (dispatcher != nullptr)
    {
        dispatcher->release();
    }
    if (physics != nullptr)
    {
        physics->release();
    }
    if (foundation != nullptr)
    {
        foundation->release();
    }
}

PxRigidActor* CreateActorForCollider(
    PxPhysics& physics,
    const PxCookingParams& cooking_params,
    const UsdPrim& prim,
    const PxTransform& pose,
    const StageMaterial& material,
    bool dynamic,
    StageResources& resources)
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
    else if (prim.IsA<UsdGeomMesh>())
    {
        const UsdGeomMesh mesh(prim);
        std::vector<PxVec3> vertices;
        std::vector<uint32_t> triangles;
        if (!ReadMeshTriangles(mesh, vertices, triangles))
        {
            px_material->release();
            return nullptr;
        }

        const UsdPhysicsMeshCollisionAPI mesh_collision(prim);
        const TfToken approximation = mesh_collision
            ? GetToken(mesh_collision.GetApproximationAttr(), TfToken("none"))
            : TfToken("none");
        if (dynamic || approximation == TfToken("convexHull"))
        {
            PxConvexMesh* convex_mesh = CookConvexMesh(physics, cooking_params, vertices);
            if (convex_mesh == nullptr)
            {
                px_material->release();
                return nullptr;
            }
            resources.convex_meshes.push_back(convex_mesh);
            geometry.storeAny(PxConvexMeshGeometry(convex_mesh));
        }
        else
        {
            PxTriangleMesh* triangle_mesh = CookTriangleMesh(physics, cooking_params, vertices, triangles);
            if (triangle_mesh == nullptr)
            {
                px_material->release();
                return nullptr;
            }
            resources.triangle_meshes.push_back(triangle_mesh);
            geometry.storeAny(PxTriangleMeshGeometry(
                triangle_mesh,
                PxMeshScale(),
                PxMeshGeometryFlag::eDOUBLE_SIDED));
        }
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

std::vector<std::string> RelationshipTargets(const UsdPrim& prim, const char* name)
{
    std::vector<std::string> paths;
    const UsdRelationship relationship = prim.GetRelationship(TfToken(name));
    if (!relationship)
    {
        return paths;
    }
    SdfPathVector targets;
    if (!relationship.GetTargets(&targets))
    {
        return paths;
    }
    paths.reserve(targets.size());
    for (const SdfPath& target : targets)
    {
        paths.push_back(target.GetString());
    }
    return paths;
}

PxRigidActor* FindTargetActor(
    const std::vector<std::string>& targets,
    const std::unordered_map<std::string, PxRigidActor*>& actors)
{
    if (targets.empty())
    {
        return nullptr;
    }
    const auto found = actors.find(targets.front());
    return found == actors.end() ? nullptr : found->second;
}

void SetActorFilterData(PxRigidActor& actor, uint32_t bit, uint32_t mask)
{
    PxFilterData filter_data(bit, mask, 0, 0);
    const PxU32 shape_count = actor.getNbShapes();
    std::vector<PxShape*> shapes(shape_count);
    actor.getShapes(shapes.data(), shape_count);
    for (PxShape* shape : shapes)
    {
        shape->setSimulationFilterData(filter_data);
    }
}

void SuppressPair(
    std::unordered_set<std::pair<std::string, std::string>, PairHash>& pairs,
    const std::string& first,
    const std::string& second)
{
    if (!first.empty() && !second.empty() && first != second)
    {
        pairs.insert(OrderedPair(first, second));
    }
}

std::unordered_set<std::pair<std::string, std::string>, PairHash> CollectSuppressedPairs(
    const UsdStageRefPtr& stage)
{
    std::unordered_set<std::pair<std::string, std::string>, PairHash> pairs;
    std::unordered_map<std::string, std::vector<std::string>> group_colliders;
    std::unordered_map<std::string, std::vector<std::string>> group_filters;
    for (const UsdPrim& prim : stage->Traverse())
    {
        if (UsdPhysicsFilteredPairsAPI(prim))
        {
            const std::string source = PrimPath(prim);
            for (const std::string& target : RelationshipTargets(prim, "physics:filteredPairs"))
            {
                SuppressPair(pairs, source, target);
            }
        }
        if (prim.IsA<UsdPhysicsCollisionGroup>())
        {
            const std::string group = PrimPath(prim);
            group_colliders[group] = RelationshipTargets(prim, "collection:colliders:includes");
            group_filters[group] = RelationshipTargets(prim, "physics:filteredGroups");
        }
    }

    for (const auto& group_filter : group_filters)
    {
        const auto source_colliders = group_colliders.find(group_filter.first);
        if (source_colliders == group_colliders.end())
        {
            continue;
        }
        for (const std::string& target_group : group_filter.second)
        {
            const auto target_colliders = group_colliders.find(target_group);
            if (target_colliders == group_colliders.end())
            {
                continue;
            }
            for (const std::string& first : source_colliders->second)
            {
                for (const std::string& second : target_colliders->second)
                {
                    SuppressPair(pairs, first, second);
                }
            }
        }
    }
    return pairs;
}

void ApplyFiltering(
    std::vector<StageActorBinding>& actors,
    const std::unordered_set<std::pair<std::string, std::string>, PairHash>& suppressed_pairs)
{
    std::unordered_map<std::string, size_t> indices;
    for (size_t index = 0; index < actors.size(); ++index)
    {
        indices.emplace(PrimPath(actors[index].prim), index);
    }
    for (const auto& pair : suppressed_pairs)
    {
        const auto first = indices.find(pair.first);
        const auto second = indices.find(pair.second);
        if (first == indices.end() || second == indices.end())
        {
            continue;
        }
        actors[first->second].mask &= ~actors[second->second].bit;
        actors[second->second].mask &= ~actors[first->second].bit;
    }
    for (const StageActorBinding& actor : actors)
    {
        SetActorFilterData(*actor.actor, actor.bit, actor.mask);
    }
}

PxJoint* CreateStageJoint(
    PxPhysics& physics,
    const UsdPrim& prim,
    const std::unordered_map<std::string, PxRigidActor*>& actors)
{
    const std::vector<std::string> body0_targets = RelationshipTargets(prim, "physics:body0");
    const std::vector<std::string> body1_targets = RelationshipTargets(prim, "physics:body1");
    PxRigidActor* actor0 = FindTargetActor(body0_targets, actors);
    PxRigidActor* actor1 = FindTargetActor(body1_targets, actors);
    if (actor0 == nullptr && actor1 == nullptr)
    {
        return nullptr;
    }

    const UsdPhysicsJoint joint(prim);
    if (joint && !GetBool(joint.GetJointEnabledAttr(), true))
    {
        return nullptr;
    }
    const PxTransform frame0 = JointFrame(
        GetVec3f(joint.GetLocalPos0Attr(), GfVec3f(0.0F)),
        GetQuatf(joint.GetLocalRot0Attr(), GfQuatf(1.0F)));
    const PxTransform frame1 = JointFrame(
        GetVec3f(joint.GetLocalPos1Attr(), GfVec3f(0.0F)),
        GetQuatf(joint.GetLocalRot1Attr(), GfQuatf(1.0F)));

    if (prim.IsA<UsdPhysicsFixedJoint>())
    {
        return PxFixedJointCreate(physics, actor0, frame0, actor1, frame1);
    }
    if (prim.IsA<UsdPhysicsRevoluteJoint>())
    {
        const UsdPhysicsRevoluteJoint revolute(prim);
        const TfToken axis = GetToken(revolute.GetAxisAttr(), TfToken("X"));
        PxRevoluteJoint* px_joint = PxRevoluteJointCreate(
            physics,
            actor0,
            AxisFrame(frame0, axis),
            actor1,
            AxisFrame(frame1, axis));
        if (px_joint == nullptr)
        {
            return nullptr;
        }
        const float lower = GetFloat(revolute.GetLowerLimitAttr(), -PxPi);
        const float upper = GetFloat(revolute.GetUpperLimitAttr(), PxPi);
        if (std::isfinite(lower) && std::isfinite(upper) && lower <= upper && (lower > -PxPi || upper < PxPi))
        {
            px_joint->setLimit(PxJointAngularLimitPair(lower, upper));
            px_joint->setRevoluteJointFlag(PxRevoluteJointFlag::eLIMIT_ENABLED, true);
        }
        const UsdPhysicsDriveAPI drive = UsdPhysicsDriveAPI::Get(prim, TfToken("angular"));
        if (drive)
        {
            const float stiffness = GetFloat(drive.GetStiffnessAttr(), 0.0F);
            const float damping = GetFloat(drive.GetDampingAttr(), 0.0F);
            const float force = GetFloat(drive.GetMaxForceAttr(), PX_MAX_F32);
            if (stiffness > 0.0F || damping > 0.0F)
            {
                px_joint->setDriveVelocity(GetFloat(drive.GetTargetVelocityAttr(), 0.0F));
                px_joint->setDriveForceLimit(force);
                px_joint->setRevoluteJointFlag(PxRevoluteJointFlag::eDRIVE_ENABLED, true);
            }
        }
        return px_joint;
    }
    if (prim.IsA<UsdPhysicsPrismaticJoint>())
    {
        const UsdPhysicsPrismaticJoint prismatic(prim);
        const TfToken axis = GetToken(prismatic.GetAxisAttr(), TfToken("X"));
        PxPrismaticJoint* px_joint = PxPrismaticJointCreate(
            physics,
            actor0,
            AxisFrame(frame0, axis),
            actor1,
            AxisFrame(frame1, axis));
        if (px_joint == nullptr)
        {
            return nullptr;
        }
        const float lower = GetFloat(prismatic.GetLowerLimitAttr(), -PX_MAX_F32);
        const float upper = GetFloat(prismatic.GetUpperLimitAttr(), PX_MAX_F32);
        if (std::isfinite(lower) && std::isfinite(upper) && lower <= upper)
        {
            px_joint->setLimit(PxJointLinearLimitPair(lower, upper, PxSpring(0.0F, 0.0F)));
            px_joint->setPrismaticJointFlag(PxPrismaticJointFlag::eLIMIT_ENABLED, true);
        }
        return px_joint;
    }
    if (prim.IsA<UsdPhysicsSphericalJoint>())
    {
        const UsdPhysicsSphericalJoint spherical(prim);
        PxSphericalJoint* px_joint = PxSphericalJointCreate(physics, actor0, frame0, actor1, frame1);
        if (px_joint == nullptr)
        {
            return nullptr;
        }
        const float y_limit = GetFloat(spherical.GetConeAngle0LimitAttr(), PxPi);
        const float z_limit = GetFloat(spherical.GetConeAngle1LimitAttr(), PxPi);
        if (y_limit < PxPi || z_limit < PxPi)
        {
            px_joint->setLimitCone(PxJointLimitCone(y_limit, z_limit));
            px_joint->setSphericalJointFlag(PxSphericalJointFlag::eLIMIT_ENABLED, true);
        }
        return px_joint;
    }
    if (prim.IsA<UsdPhysicsDistanceJoint>())
    {
        const UsdPhysicsDistanceJoint distance(prim);
        PxDistanceJoint* px_joint = PxDistanceJointCreate(physics, actor0, frame0, actor1, frame1);
        if (px_joint == nullptr)
        {
            return nullptr;
        }
        const float min_distance = GetFloat(distance.GetMinDistanceAttr(), 0.0F);
        const float max_distance = GetFloat(distance.GetMaxDistanceAttr(), PX_MAX_F32);
        px_joint->setMinDistance(std::max(0.0F, min_distance));
        px_joint->setMaxDistance(std::max(min_distance, max_distance));
        px_joint->setDistanceJointFlag(PxDistanceJointFlag::eMIN_DISTANCE_ENABLED, min_distance > 0.0F);
        px_joint->setDistanceJointFlag(PxDistanceJointFlag::eMAX_DISTANCE_ENABLED, max_distance < PX_MAX_F32);
        return px_joint;
    }
    return nullptr;
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
    const PxCookingParams cooking_params{PxTolerancesScale()};
    PxDefaultCpuDispatcher* dispatcher = PxDefaultCpuDispatcherCreate(2);
    PxSceneDesc scene_desc(physics->getTolerancesScale());
    scene_desc.gravity = PxVec3(
        gravity_direction[0] * gravity_magnitude,
        gravity_direction[1] * gravity_magnitude,
        gravity_direction[2] * gravity_magnitude);
    scene_desc.cpuDispatcher = dispatcher;
    scene_desc.filterShader = StageFilterShader;
    PxScene* scene = physics->createScene(scene_desc);
    if (scene == nullptr)
    {
        dispatcher->release();
        physics->release();
        foundation->release();
        WriteError(error, "PxPhysics::createScene failed.");
        return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
    }

    StageResources resources;
    std::vector<StageBodyBinding> bodies;
    std::vector<StageActorBinding> actor_bindings;
    std::unordered_map<std::string, PxRigidActor*> actor_map;
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
            actor = CreateActorForCollider(*physics, cooking_params, prim, pose, material, dynamic, resources);
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
        const uint32_t bit = actor_bindings.size() < 31 ? (1U << actor_bindings.size()) : 0x80000000U;
        actor_bindings.push_back({prim, actor, bit, 0xFFFFFFFFU});
        actor_map[PrimPath(prim)] = actor;
    }

    ApplyFiltering(actor_bindings, CollectSuppressedPairs(stage));
    for (const UsdPrim& prim : stage->Traverse())
    {
        if (PxJoint* joint = CreateStageJoint(*physics, prim, actor_map))
        {
            resources.joints.push_back(joint);
        }
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
            ReleaseStageSimulation(scene, dispatcher, physics, foundation, resources);
            return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
        }
    }
    if (!mark.IsClean())
    {
        WriteError(error, "OpenUSD reported an error while writing simulated transforms.");
        ReleaseStageSimulation(scene, dispatcher, physics, foundation, resources);
        return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
    }
    if (!stage->GetRootLayer()->Save())
    {
        WriteError(error, "Could not save simulated stage transforms.");
        ReleaseStageSimulation(scene, dispatcher, physics, foundation, resources);
        return OPENUSD_PHYSX_STATUS_NATIVE_ERROR;
    }

    ReleaseStageSimulation(scene, dispatcher, physics, foundation, resources);
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
