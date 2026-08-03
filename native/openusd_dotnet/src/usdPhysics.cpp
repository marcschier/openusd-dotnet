// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

#include "pxr/usd/usdPhysics/articulationRootAPI.h"
#include "pxr/usd/usdPhysics/collisionAPI.h"
#include "pxr/usd/usdPhysics/collisionGroup.h"
#include "pxr/usd/usdPhysics/distanceJoint.h"
#include "pxr/usd/usdPhysics/driveAPI.h"
#include "pxr/usd/usdPhysics/filteredPairsAPI.h"
#include "pxr/usd/usdPhysics/fixedJoint.h"
#include "pxr/usd/usdPhysics/joint.h"
#include "pxr/usd/usdPhysics/limitAPI.h"
#include "pxr/usd/usdPhysics/massAPI.h"
#include "pxr/usd/usdPhysics/materialAPI.h"
#include "pxr/usd/usdPhysics/meshCollisionAPI.h"
#include "pxr/usd/usdPhysics/prismaticJoint.h"
#include "pxr/usd/usdPhysics/revoluteJoint.h"
#include "pxr/usd/usdPhysics/rigidBodyAPI.h"
#include "pxr/usd/usdPhysics/scene.h"
#include "pxr/usd/usdPhysics/sphericalJoint.h"

namespace
{
bool ValidSchema(openusd_physics_schema_kind kind)
{
    return kind >= OPENUSD_PHYSICS_SCHEMA_SCENE && kind <= OPENUSD_PHYSICS_SCHEMA_FIXED_JOINT;
}

bool ValidApi(openusd_physics_api_kind kind)
{
    return kind >= OPENUSD_PHYSICS_API_RIGID_BODY && kind <= OPENUSD_PHYSICS_API_DRIVE;
}

bool NeedsInstance(openusd_physics_api_kind kind)
{
    return kind == OPENUSD_PHYSICS_API_LIMIT || kind == OPENUSD_PHYSICS_API_DRIVE;
}

bool ValidInstance(openusd_physics_api_kind kind, const char* instance)
{
    return !NeedsInstance(kind) || (instance != nullptr && instance[0] != '\0');
}

TfToken Instance(const char* value)
{
    return TfToken(value == nullptr ? "" : value);
}

openusd_status GetPrim(
    const openusd_stage* stage,
    const char* prim_path,
    UsdPrim* prim,
    openusd_error_buffer* error)
{
    if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) || prim == nullptr)
    {
        WriteError(error, "A valid stage, absolute physics prim path, and prim output are required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    *prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
    if (!*prim)
    {
        WriteError(error, std::string("Prim was not found: ") + prim_path);
        return OPENUSD_STATUS_NOT_FOUND;
    }
    return OPENUSD_STATUS_OK;
}

bool IsSchema(const UsdPrim& prim, openusd_physics_schema_kind kind)
{
    switch (kind)
    {
        case OPENUSD_PHYSICS_SCHEMA_SCENE: return prim.IsA<UsdPhysicsScene>();
        case OPENUSD_PHYSICS_SCHEMA_COLLISION_GROUP: return prim.IsA<UsdPhysicsCollisionGroup>();
        case OPENUSD_PHYSICS_SCHEMA_JOINT: return prim.IsA<UsdPhysicsJoint>();
        case OPENUSD_PHYSICS_SCHEMA_REVOLUTE_JOINT: return prim.IsA<UsdPhysicsRevoluteJoint>();
        case OPENUSD_PHYSICS_SCHEMA_PRISMATIC_JOINT: return prim.IsA<UsdPhysicsPrismaticJoint>();
        case OPENUSD_PHYSICS_SCHEMA_SPHERICAL_JOINT: return prim.IsA<UsdPhysicsSphericalJoint>();
        case OPENUSD_PHYSICS_SCHEMA_DISTANCE_JOINT: return prim.IsA<UsdPhysicsDistanceJoint>();
        case OPENUSD_PHYSICS_SCHEMA_FIXED_JOINT: return prim.IsA<UsdPhysicsFixedJoint>();
        default: return false;
    }
}

const SdfValueTypeName& FloatType()
{
    return SdfValueTypeNames->Float;
}

const SdfValueTypeName& BoolType()
{
    return SdfValueTypeNames->Bool;
}

const SdfValueTypeName& Vec3fType(openusd_physics_vec3f_property property)
{
    if (property == OPENUSD_PHYSICS_VEC3F_MASS_CENTER_OF_MASS ||
        property == OPENUSD_PHYSICS_VEC3F_JOINT_LOCAL_POS0 ||
        property == OPENUSD_PHYSICS_VEC3F_JOINT_LOCAL_POS1)
    {
        return SdfValueTypeNames->Point3f;
    }
    if (property == OPENUSD_PHYSICS_VEC3F_MASS_DIAGONAL_INERTIA)
    {
        return SdfValueTypeNames->Float3;
    }
    return SdfValueTypeNames->Vector3f;
}

const SdfValueTypeName& QuatfType()
{
    return SdfValueTypeNames->Quatf;
}

const SdfValueTypeName& TokenType()
{
    return SdfValueTypeNames->Token;
}

const SdfValueTypeName& StringType()
{
    return SdfValueTypeNames->String;
}

std::string MultiName(const char* family, const char* instance, const char* base)
{
    return std::string("physics:") + family + ":" + instance + ":" + base;
}

const char* FloatName(openusd_physics_float_property property)
{
    switch (property)
    {
        case OPENUSD_PHYSICS_FLOAT_SCENE_GRAVITY_MAGNITUDE: return "physics:gravityMagnitude";
        case OPENUSD_PHYSICS_FLOAT_MASS_MASS: return "physics:mass";
        case OPENUSD_PHYSICS_FLOAT_MASS_DENSITY: return "physics:density";
        case OPENUSD_PHYSICS_FLOAT_MATERIAL_DYNAMIC_FRICTION: return "physics:dynamicFriction";
        case OPENUSD_PHYSICS_FLOAT_MATERIAL_STATIC_FRICTION: return "physics:staticFriction";
        case OPENUSD_PHYSICS_FLOAT_MATERIAL_RESTITUTION: return "physics:restitution";
        case OPENUSD_PHYSICS_FLOAT_MATERIAL_DENSITY: return "physics:density";
        case OPENUSD_PHYSICS_FLOAT_JOINT_BREAK_FORCE: return "physics:breakForce";
        case OPENUSD_PHYSICS_FLOAT_JOINT_BREAK_TORQUE: return "physics:breakTorque";
        case OPENUSD_PHYSICS_FLOAT_REVOLUTE_LOWER_LIMIT: return "physics:lowerLimit";
        case OPENUSD_PHYSICS_FLOAT_REVOLUTE_UPPER_LIMIT: return "physics:upperLimit";
        case OPENUSD_PHYSICS_FLOAT_PRISMATIC_LOWER_LIMIT: return "physics:lowerLimit";
        case OPENUSD_PHYSICS_FLOAT_PRISMATIC_UPPER_LIMIT: return "physics:upperLimit";
        case OPENUSD_PHYSICS_FLOAT_SPHERICAL_CONE_ANGLE0_LIMIT: return "physics:coneAngle0Limit";
        case OPENUSD_PHYSICS_FLOAT_SPHERICAL_CONE_ANGLE1_LIMIT: return "physics:coneAngle1Limit";
        case OPENUSD_PHYSICS_FLOAT_DISTANCE_MIN_DISTANCE: return "physics:minDistance";
        case OPENUSD_PHYSICS_FLOAT_DISTANCE_MAX_DISTANCE: return "physics:maxDistance";
        default: return nullptr;
    }
}

std::string FloatName(openusd_physics_float_property property, const char* instance)
{
    if (property == OPENUSD_PHYSICS_FLOAT_LIMIT_LOW) return MultiName("limit", instance, "low");
    if (property == OPENUSD_PHYSICS_FLOAT_LIMIT_HIGH) return MultiName("limit", instance, "high");
    if (property == OPENUSD_PHYSICS_FLOAT_DRIVE_MAX_FORCE) return MultiName("drive", instance, "maxForce");
    if (property == OPENUSD_PHYSICS_FLOAT_DRIVE_TARGET_POSITION) return MultiName("drive", instance, "targetPosition");
    if (property == OPENUSD_PHYSICS_FLOAT_DRIVE_TARGET_VELOCITY) return MultiName("drive", instance, "targetVelocity");
    if (property == OPENUSD_PHYSICS_FLOAT_DRIVE_DAMPING) return MultiName("drive", instance, "damping");
    if (property == OPENUSD_PHYSICS_FLOAT_DRIVE_STIFFNESS) return MultiName("drive", instance, "stiffness");
    const char* name = FloatName(property);
    return name == nullptr ? std::string() : std::string(name);
}

bool FloatNeedsInstance(openusd_physics_float_property property)
{
    return property >= OPENUSD_PHYSICS_FLOAT_LIMIT_LOW;
}

const char* BoolName(openusd_physics_bool_property property)
{
    switch (property)
    {
        case OPENUSD_PHYSICS_BOOL_RIGID_BODY_ENABLED: return "physics:rigidBodyEnabled";
        case OPENUSD_PHYSICS_BOOL_RIGID_BODY_KINEMATIC_ENABLED: return "physics:kinematicEnabled";
        case OPENUSD_PHYSICS_BOOL_RIGID_BODY_STARTS_ASLEEP: return "physics:startsAsleep";
        case OPENUSD_PHYSICS_BOOL_COLLISION_ENABLED: return "physics:collisionEnabled";
        case OPENUSD_PHYSICS_BOOL_COLLISION_GROUP_INVERT_FILTERED_GROUPS:
            return "physics:invertFilteredGroups";
        case OPENUSD_PHYSICS_BOOL_JOINT_ENABLED: return "physics:jointEnabled";
        case OPENUSD_PHYSICS_BOOL_JOINT_COLLISION_ENABLED: return "physics:collisionEnabled";
        case OPENUSD_PHYSICS_BOOL_JOINT_EXCLUDE_FROM_ARTICULATION:
            return "physics:excludeFromArticulation";
        default: return nullptr;
    }
}

const char* Vec3fName(openusd_physics_vec3f_property property)
{
    switch (property)
    {
        case OPENUSD_PHYSICS_VEC3F_SCENE_GRAVITY_DIRECTION: return "physics:gravityDirection";
        case OPENUSD_PHYSICS_VEC3F_RIGID_BODY_VELOCITY: return "physics:velocity";
        case OPENUSD_PHYSICS_VEC3F_RIGID_BODY_ANGULAR_VELOCITY: return "physics:angularVelocity";
        case OPENUSD_PHYSICS_VEC3F_MASS_CENTER_OF_MASS: return "physics:centerOfMass";
        case OPENUSD_PHYSICS_VEC3F_MASS_DIAGONAL_INERTIA: return "physics:diagonalInertia";
        case OPENUSD_PHYSICS_VEC3F_JOINT_LOCAL_POS0: return "physics:localPos0";
        case OPENUSD_PHYSICS_VEC3F_JOINT_LOCAL_POS1: return "physics:localPos1";
        default: return nullptr;
    }
}

const char* QuatfName(openusd_physics_quatf_property property)
{
    switch (property)
    {
        case OPENUSD_PHYSICS_QUATF_MASS_PRINCIPAL_AXES: return "physics:principalAxes";
        case OPENUSD_PHYSICS_QUATF_JOINT_LOCAL_ROT0: return "physics:localRot0";
        case OPENUSD_PHYSICS_QUATF_JOINT_LOCAL_ROT1: return "physics:localRot1";
        default: return nullptr;
    }
}

std::string TokenName(openusd_physics_token_property property, const char* instance)
{
    switch (property)
    {
        case OPENUSD_PHYSICS_TOKEN_MESH_COLLISION_APPROXIMATION: return "physics:approximation";
        case OPENUSD_PHYSICS_TOKEN_REVOLUTE_AXIS: return "physics:axis";
        case OPENUSD_PHYSICS_TOKEN_PRISMATIC_AXIS: return "physics:axis";
        case OPENUSD_PHYSICS_TOKEN_SPHERICAL_AXIS: return "physics:axis";
        case OPENUSD_PHYSICS_TOKEN_DRIVE_TYPE: return MultiName("drive", instance, "type");
        default: return std::string();
    }
}

bool TokenNeedsInstance(openusd_physics_token_property property)
{
    return property == OPENUSD_PHYSICS_TOKEN_DRIVE_TYPE;
}

template <typename TValue>
openusd_status SetAttribute(
    const UsdPrim& prim,
    const std::string& name,
    const SdfValueTypeName& type,
    const TValue& value,
    const char* label,
    openusd_error_buffer* error)
{
    if (name.empty())
    {
        WriteError(error, std::string("The requested ") + label + " is unsupported.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return SetLuxAttribute(prim.CreateAttribute(TfToken(name), type, true), value, label, error);
}

template <typename TValue>
openusd_status GetAttribute(
    const UsdPrim& prim,
    const std::string& name,
    const SdfValueTypeName& type,
    TValue* value,
    const char* label,
    openusd_error_buffer* error)
{
    if (name.empty())
    {
        WriteError(error, std::string("The requested ") + label + " is unsupported.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    const UsdAttribute attribute = prim.GetAttribute(TfToken(name));
    const openusd_status typeStatus = ValidateAttributeType(attribute, type, label, error);
    if (typeStatus != OPENUSD_STATUS_OK)
    {
        return typeStatus;
    }
    return GetLuxAttribute(attribute, value, label, error);
}

GfVec3f ToGf(openusd_vec3f value)
{
    return GfVec3f(value.x, value.y, value.z);
}

openusd_vec3f FromGf(const GfVec3f& value)
{
    return openusd_vec3f{value[0], value[1], value[2]};
}

GfQuatf ToGf(openusd_quatf value)
{
    return GfQuatf(value.real, GfVec3f(value.x, value.y, value.z));
}

openusd_quatf FromGf(const GfQuatf& value)
{
    const GfVec3f imaginary = value.GetImaginary();
    return openusd_quatf{value.GetReal(), imaginary[0], imaginary[1], imaginary[2]};
}
} // namespace

openusd_status openusd_physics_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_schema_kind schema_kind,
    int32_t* is_schema,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(is_schema);
        if (!ValidSchema(schema_kind) || is_schema == nullptr)
        {
            WriteError(error, "A valid physics schema kind and result are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            const openusd_status status = GetPrim(stage, prim_path, &prim, error);
            if (status != OPENUSD_STATUS_OK) return status;
            *is_schema = IsSchema(prim, schema_kind) ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_physics_define(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_schema_kind schema_kind,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) || !ValidSchema(schema_kind))
        {
            WriteError(error, "A valid stage, absolute physics prim path, and schema kind are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const SdfPath path(prim_path);
            UsdPrim prim;
            switch (schema_kind)
            {
                case OPENUSD_PHYSICS_SCHEMA_SCENE: prim = UsdPhysicsScene::Define(stage->value, path).GetPrim(); break;
                case OPENUSD_PHYSICS_SCHEMA_COLLISION_GROUP:
                    prim = UsdPhysicsCollisionGroup::Define(stage->value, path).GetPrim(); break;
                case OPENUSD_PHYSICS_SCHEMA_JOINT: prim = UsdPhysicsJoint::Define(stage->value, path).GetPrim(); break;
                case OPENUSD_PHYSICS_SCHEMA_REVOLUTE_JOINT:
                    prim = UsdPhysicsRevoluteJoint::Define(stage->value, path).GetPrim(); break;
                case OPENUSD_PHYSICS_SCHEMA_PRISMATIC_JOINT:
                    prim = UsdPhysicsPrismaticJoint::Define(stage->value, path).GetPrim(); break;
                case OPENUSD_PHYSICS_SCHEMA_SPHERICAL_JOINT:
                    prim = UsdPhysicsSphericalJoint::Define(stage->value, path).GetPrim(); break;
                case OPENUSD_PHYSICS_SCHEMA_DISTANCE_JOINT:
                    prim = UsdPhysicsDistanceJoint::Define(stage->value, path).GetPrim(); break;
                case OPENUSD_PHYSICS_SCHEMA_FIXED_JOINT:
                    prim = UsdPhysicsFixedJoint::Define(stage->value, path).GetPrim(); break;
                default: break;
            }
            if (!prim || !IsSchema(prim, schema_kind))
            {
                WriteError(error, "Could not define the requested UsdPhysics schema.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_physics_has_api(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_api_kind api_kind,
    const char* instance_name,
    int32_t* has_api,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(has_api);
        if (!ValidApi(api_kind) || !ValidInstance(api_kind, instance_name) || has_api == nullptr)
        {
            WriteError(error, "A valid physics API kind, instance name, and result are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            const openusd_status status = GetPrim(stage, prim_path, &prim, error);
            if (status != OPENUSD_STATUS_OK) return status;
            bool has = false;
            switch (api_kind)
            {
                case OPENUSD_PHYSICS_API_RIGID_BODY: has = prim.HasAPI<UsdPhysicsRigidBodyAPI>(); break;
                case OPENUSD_PHYSICS_API_MASS: has = prim.HasAPI<UsdPhysicsMassAPI>(); break;
                case OPENUSD_PHYSICS_API_COLLISION: has = prim.HasAPI<UsdPhysicsCollisionAPI>(); break;
                case OPENUSD_PHYSICS_API_MESH_COLLISION: has = prim.HasAPI<UsdPhysicsMeshCollisionAPI>(); break;
                case OPENUSD_PHYSICS_API_MATERIAL: has = prim.HasAPI<UsdPhysicsMaterialAPI>(); break;
                case OPENUSD_PHYSICS_API_FILTERED_PAIRS: has = prim.HasAPI<UsdPhysicsFilteredPairsAPI>(); break;
                case OPENUSD_PHYSICS_API_ARTICULATION_ROOT: has = prim.HasAPI<UsdPhysicsArticulationRootAPI>(); break;
                case OPENUSD_PHYSICS_API_LIMIT: has = prim.HasAPI<UsdPhysicsLimitAPI>(Instance(instance_name)); break;
                case OPENUSD_PHYSICS_API_DRIVE: has = prim.HasAPI<UsdPhysicsDriveAPI>(Instance(instance_name)); break;
                default: break;
            }
            *has_api = has ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_physics_apply_api(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_api_kind api_kind,
    const char* instance_name,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!ValidApi(api_kind) || !ValidInstance(api_kind, instance_name))
        {
            WriteError(error, "A valid physics API kind and instance name are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            const openusd_status status = GetPrim(stage, prim_path, &prim, error);
            if (status != OPENUSD_STATUS_OK) return status;
            bool applied = false;
            switch (api_kind)
            {
                case OPENUSD_PHYSICS_API_RIGID_BODY: applied = bool(UsdPhysicsRigidBodyAPI::Apply(prim)); break;
                case OPENUSD_PHYSICS_API_MASS: applied = bool(UsdPhysicsMassAPI::Apply(prim)); break;
                case OPENUSD_PHYSICS_API_COLLISION: applied = bool(UsdPhysicsCollisionAPI::Apply(prim)); break;
                case OPENUSD_PHYSICS_API_MESH_COLLISION: applied = bool(UsdPhysicsMeshCollisionAPI::Apply(prim)); break;
                case OPENUSD_PHYSICS_API_MATERIAL: applied = bool(UsdPhysicsMaterialAPI::Apply(prim)); break;
                case OPENUSD_PHYSICS_API_FILTERED_PAIRS: applied = bool(UsdPhysicsFilteredPairsAPI::Apply(prim)); break;
                case OPENUSD_PHYSICS_API_ARTICULATION_ROOT:
                    applied = bool(UsdPhysicsArticulationRootAPI::Apply(prim)); break;
                case OPENUSD_PHYSICS_API_LIMIT:
                    applied = bool(UsdPhysicsLimitAPI::Apply(prim, Instance(instance_name))); break;
                case OPENUSD_PHYSICS_API_DRIVE:
                    applied = bool(UsdPhysicsDriveAPI::Apply(prim, Instance(instance_name))); break;
                default: break;
            }
            if (!applied)
            {
                WriteError(error, "Could not apply the requested UsdPhysics API schema.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_physics_set_float(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_float_property property,
    const char* instance_name,
    float value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!std::isfinite(value) && property != OPENUSD_PHYSICS_FLOAT_SCENE_GRAVITY_MAGNITUDE)
        {
            WriteError(error, "A finite physics float value is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (FloatNeedsInstance(property) && (instance_name == nullptr || instance_name[0] == '\0'))
        {
            WriteError(error, "A physics multiple-apply instance name is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            const openusd_status status = GetPrim(stage, prim_path, &prim, error);
            if (status != OPENUSD_STATUS_OK) return status;
            return SetAttribute(prim, FloatName(property, instance_name), FloatType(), value, "physics float", error);
        });
    });
}

openusd_status openusd_physics_get_float(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_float_property property,
    const char* instance_name,
    float* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (value == nullptr || (FloatNeedsInstance(property) && (instance_name == nullptr || instance_name[0] == '\0')))
        {
            WriteError(error, "A physics float output and valid instance name are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            const openusd_status status = GetPrim(stage, prim_path, &prim, error);
            if (status != OPENUSD_STATUS_OK) return status;
            return GetAttribute(prim, FloatName(property, instance_name), FloatType(), value, "physics float", error);
        });
    });
}

openusd_status openusd_physics_set_bool(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_bool_property property,
    int32_t value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        return Guard(error, [&]()
        {
            UsdPrim prim;
            const openusd_status status = GetPrim(stage, prim_path, &prim, error);
            if (status != OPENUSD_STATUS_OK) return status;
            const char* name = BoolName(property);
            return SetAttribute(prim, name == nullptr ? std::string() : name, BoolType(), value != 0, "physics bool", error);
        });
    });
}

openusd_status openusd_physics_get_bool(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_bool_property property,
    int32_t* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (value == nullptr)
        {
            WriteError(error, "A physics bool output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            bool read = false;
            const openusd_status status = GetPrim(stage, prim_path, &prim, error);
            if (status != OPENUSD_STATUS_OK) return status;
            const char* name = BoolName(property);
            const openusd_status getStatus = GetAttribute(
                prim, name == nullptr ? std::string() : name, BoolType(), &read, "physics bool", error);
            *value = read ? 1 : 0;
            return getStatus;
        });
    });
}

openusd_status openusd_physics_set_vec3f(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_vec3f_property property,
    openusd_vec3f value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!std::isfinite(value.x) || !std::isfinite(value.y) || !std::isfinite(value.z))
        {
            WriteError(error, "A finite physics vec3f value is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            const openusd_status status = GetPrim(stage, prim_path, &prim, error);
            if (status != OPENUSD_STATUS_OK) return status;
            const char* name = Vec3fName(property);
            return SetAttribute(
                prim, name == nullptr ? std::string() : name, Vec3fType(property), ToGf(value), "physics vec3f", error);
        });
    });
}

openusd_status openusd_physics_get_vec3f(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_vec3f_property property,
    openusd_vec3f* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (value == nullptr)
        {
            WriteError(error, "A physics vec3f output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            GfVec3f read(0.0F);
            const openusd_status status = GetPrim(stage, prim_path, &prim, error);
            if (status != OPENUSD_STATUS_OK) return status;
            const char* name = Vec3fName(property);
            const openusd_status getStatus = GetAttribute(
                prim, name == nullptr ? std::string() : name, Vec3fType(property), &read, "physics vec3f", error);
            *value = FromGf(read);
            return getStatus;
        });
    });
}

openusd_status openusd_physics_set_quatf(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_quatf_property property,
    openusd_quatf value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        return Guard(error, [&]()
        {
            UsdPrim prim;
            const openusd_status status = GetPrim(stage, prim_path, &prim, error);
            if (status != OPENUSD_STATUS_OK) return status;
            const char* name = QuatfName(property);
            return SetAttribute(
                prim, name == nullptr ? std::string() : name, QuatfType(), ToGf(value), "physics quatf", error);
        });
    });
}

openusd_status openusd_physics_get_quatf(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_quatf_property property,
    openusd_quatf* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (value == nullptr)
        {
            WriteError(error, "A physics quatf output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            GfQuatf read(1.0F);
            const openusd_status status = GetPrim(stage, prim_path, &prim, error);
            if (status != OPENUSD_STATUS_OK) return status;
            const char* name = QuatfName(property);
            const openusd_status getStatus = GetAttribute(
                prim, name == nullptr ? std::string() : name, QuatfType(), &read, "physics quatf", error);
            *value = FromGf(read);
            return getStatus;
        });
    });
}

openusd_status openusd_physics_set_token(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_token_property property,
    const char* instance_name,
    const char* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (value == nullptr || value[0] == '\0' ||
            (TokenNeedsInstance(property) && (instance_name == nullptr || instance_name[0] == '\0')))
        {
            WriteError(error, "A physics token value and valid instance name are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            const openusd_status status = GetPrim(stage, prim_path, &prim, error);
            if (status != OPENUSD_STATUS_OK) return status;
            return SetAttribute(
                prim, TokenName(property, instance_name), TokenType(), TfToken(value), "physics token", error);
        });
    });
}

openusd_status openusd_physics_get_token(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_token_property property,
    const char* instance_name,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiStringOutput(buffer, capacity);        ResetAbiOutput(required);
        if (required == nullptr ||
            (TokenNeedsInstance(property) && (instance_name == nullptr || instance_name[0] == '\0')))
        {
            WriteError(error, "A physics token output and valid instance name are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            TfToken token;
            const openusd_status status = GetPrim(stage, prim_path, &prim, error);
            if (status != OPENUSD_STATUS_OK) return status;
            const openusd_status getStatus = GetAttribute(
                prim, TokenName(property, instance_name), TokenType(), &token, "physics token", error);
            if (getStatus != OPENUSD_STATUS_OK) return getStatus;
            return CopyString(token.GetString(), buffer, capacity, required);
        });
    });
}

openusd_status openusd_physics_set_string(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_string_property property,
    const char* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (property != OPENUSD_PHYSICS_STRING_COLLISION_GROUP_MERGE_GROUP_NAME || value == nullptr)
        {
            WriteError(error, "A valid physics string property and value are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            const openusd_status status = GetPrim(stage, prim_path, &prim, error);
            if (status != OPENUSD_STATUS_OK) return status;
            return SetAttribute(
                prim, "physics:mergeGroup", StringType(), std::string(value), "physics string", error);
        });
    });
}

openusd_status openusd_physics_get_string(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_string_property property,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiStringOutput(buffer, capacity);        ResetAbiOutput(required);
        if (property != OPENUSD_PHYSICS_STRING_COLLISION_GROUP_MERGE_GROUP_NAME || required == nullptr)
        {
            WriteError(error, "A valid physics string property and output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            std::string value;
            const openusd_status status = GetPrim(stage, prim_path, &prim, error);
            if (status != OPENUSD_STATUS_OK) return status;
            const openusd_status getStatus =
                GetAttribute(prim, "physics:mergeGroup", StringType(), &value, "physics string", error);
            if (getStatus != OPENUSD_STATUS_OK) return getStatus;
            return CopyString(value, buffer, capacity, required);
        });
    });
}
