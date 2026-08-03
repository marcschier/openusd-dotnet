// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

openusd_status openusd_skel_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_schema_kind schema_kind,
    int32_t* is_schema,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(is_schema);
        if (is_schema == nullptr || schema_kind < OPENUSD_SKEL_SCHEMA_ROOT ||
            schema_kind > OPENUSD_SKEL_SCHEMA_ANIMATION)
        {
            WriteError(error, "A valid skeleton schema kind and result are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            const openusd_status status = GetSkelPrim(stage, prim_path, &prim, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            *is_schema = IsSkelSchema(prim, schema_kind) ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_skel_define(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_schema_kind schema_kind,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            schema_kind < OPENUSD_SKEL_SCHEMA_ROOT ||
            schema_kind > OPENUSD_SKEL_SCHEMA_ANIMATION)
        {
            WriteError(error, "A valid stage, absolute prim path, and skeleton schema kind are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const SdfPath path(prim_path);
            UsdPrim prim;
            switch (schema_kind)
            {
                case OPENUSD_SKEL_SCHEMA_ROOT:
                    prim = UsdSkelRoot::Define(stage->value, path).GetPrim();
                    break;
                case OPENUSD_SKEL_SCHEMA_SKELETON:
                    prim = UsdSkelSkeleton::Define(stage->value, path).GetPrim();
                    break;
                case OPENUSD_SKEL_SCHEMA_ANIMATION:
                    prim = UsdSkelAnimation::Define(stage->value, path).GetPrim();
                    break;
                default:
                    break;
            }
            if (!prim || !IsSkelSchema(prim, schema_kind))
            {
                WriteError(error, "Could not define the requested UsdSkel schema.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_skel_has_binding(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* has_binding,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(has_binding);
        if (has_binding == nullptr)
        {
            WriteError(error, "A skeleton binding result is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            const openusd_status status = GetSkelPrim(stage, prim_path, &prim, error);
            if (status == OPENUSD_STATUS_OK)
            {
                *has_binding = prim.HasAPI<UsdSkelBindingAPI>() ? 1 : 0;
            }
            return status;
        });

    });
}

openusd_status openusd_skel_apply_binding(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        return Guard(error, [&]()
        {
            UsdPrim prim;
            const openusd_status status = GetSkelPrim(stage, prim_path, &prim, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            if (prim.HasAPI<UsdSkelBindingAPI>())
            {
                return OPENUSD_STATUS_OK;
            }
            std::string whyNot;
            if (!UsdSkelBindingAPI::CanApply(prim, &whyNot))
            {
                WriteError(
                    error,
                    whyNot.empty() ? "UsdSkelBindingAPI cannot be applied." : whyNot);
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            if (!UsdSkelBindingAPI::Apply(prim))
            {
                WriteError(error, "Could not apply UsdSkelBindingAPI.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_skel_set_joints(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_schema_kind schema_kind,
    const openusd_string_list_view* joints,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (schema_kind != OPENUSD_SKEL_SCHEMA_SKELETON &&
            schema_kind != OPENUSD_SKEL_SCHEMA_ANIMATION)
        {
            WriteError(error, "Joints are supported only on Skeleton and SkelAnimation schemas.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            VtTokenArray values;
            openusd_status status = ReadSkelTokens(
                joints, schema_kind == OPENUSD_SKEL_SCHEMA_SKELETON, &values, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }

            if (schema_kind == OPENUSD_SKEL_SCHEMA_SKELETON)
            {
                UsdSkelSkeleton skeleton;
                status = GetSkelSkeleton(stage, prim_path, &skeleton, error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                status = ValidateAuthoredArrayCardinality<GfMatrix4d>(
                    skeleton.GetBindTransformsAttr(), values.size(), "bindTransforms", error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                status = ValidateAuthoredArrayCardinality<GfMatrix4d>(
                    skeleton.GetRestTransformsAttr(), values.size(), "restTransforms", error);
                return status == OPENUSD_STATUS_OK
                    ? SetLuxAttribute(skeleton.CreateJointsAttr(), values, "skeleton joints", error)
                    : status;
            }

            UsdSkelAnimation animation;
            status = GetSkelAnimation(stage, prim_path, &animation, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            status = ValidateAuthoredArrayCardinality<GfVec3f>(
                animation.GetTranslationsAttr(), values.size(), "translations", error);
            if (status == OPENUSD_STATUS_OK)
            {
                status = ValidateAuthoredArrayCardinality<GfQuatf>(
                    animation.GetRotationsAttr(), values.size(), "rotations", error);
            }
            if (status == OPENUSD_STATUS_OK)
            {
                status = ValidateAuthoredArrayCardinality<GfVec3h>(
                    animation.GetScalesAttr(), values.size(), "scales", error);
            }
            return status == OPENUSD_STATUS_OK
                ? SetLuxAttribute(animation.CreateJointsAttr(), values, "animation joints", error)
                : status;
        });

    });
}

openusd_status openusd_skel_get_joints(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_schema_kind schema_kind,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetStringListOutput(list, view);
        if ((schema_kind != OPENUSD_SKEL_SCHEMA_SKELETON &&
             schema_kind != OPENUSD_SKEL_SCHEMA_ANIMATION) ||
            list == nullptr || view == nullptr ||
            view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A supported schema and versioned joint-list outputs are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            VtTokenArray joints;
            openusd_status status;
            if (schema_kind == OPENUSD_SKEL_SCHEMA_SKELETON)
            {
                UsdSkelSkeleton skeleton;
                status = GetSkelSkeleton(stage, prim_path, &skeleton, error);
                if (status == OPENUSD_STATUS_OK && !skeleton.GetJointsAttr().Get(&joints))
                {
                    WriteError(error, "The skeleton has no authored joints.");
                    status = OPENUSD_STATUS_NOT_FOUND;
                }
            }
            else
            {
                UsdSkelAnimation animation;
                status = GetSkelAnimation(stage, prim_path, &animation, error);
                if (status == OPENUSD_STATUS_OK && !animation.GetJointsAttr().Get(&joints))
                {
                    WriteError(error, "The animation has no authored joints.");
                    status = OPENUSD_STATUS_NOT_FOUND;
                }
            }
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), ToStrings(joints), view);
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_skel_set_skeleton_matrices(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_matrix_property property,
    const openusd_matrix4d* values,
    size_t count,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!IsValidArrayBuffer(values, count))
        {
            WriteError(error, "An aligned matrix buffer and non-overflowing count are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        for (size_t index = 0; index < count; ++index)
        {
            if (!IsFiniteMatrix(values[index]))
            {
                WriteError(error, "Skeleton matrices must contain only finite values.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
        }
        return Guard(error, [&]()
        {
            UsdSkelSkeleton skeleton;
            openusd_status status = GetSkelSkeleton(stage, prim_path, &skeleton, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            size_t jointCount = 0;
            status = GetSkeletonJointCount(skeleton, &jointCount, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            if (count != jointCount)
            {
                WriteError(error, "Skeleton matrix cardinality must match joints.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            UsdAttribute attribute;
            if (property == OPENUSD_SKEL_MATRIX_BIND_TRANSFORMS)
            {
                attribute = skeleton.CreateBindTransformsAttr();
            }
            else if (property == OPENUSD_SKEL_MATRIX_REST_TRANSFORMS)
            {
                attribute = skeleton.CreateRestTransformsAttr();
            }
            else
            {
                WriteError(error, "The requested skeleton matrix property is unsupported.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            return SetSchemaArray<openusd_matrix4d, GfMatrix4d>(
                attribute,
                values,
                count,
                UsdTimeCode::Default(),
                SdfValueTypeNames->Matrix4dArray,
                "skeleton matrices",
                [](const openusd_matrix4d& value) { return ToMatrix4d(value); },
                error);
        });

    });
}

openusd_status openusd_skel_get_skeleton_matrices(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_matrix_property property,
    openusd_matrix4d* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(required);
        return WithAbiWritableBuffer(values, capacity, [&]()
        {
            return Guard(error, [&]()
            {
                UsdSkelSkeleton skeleton;
                const openusd_status status =
                    GetSkelSkeleton(stage, prim_path, &skeleton, error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                UsdAttribute attribute;
                if (property == OPENUSD_SKEL_MATRIX_BIND_TRANSFORMS)
                {
                    attribute = skeleton.GetBindTransformsAttr();
                }
                else if (property == OPENUSD_SKEL_MATRIX_REST_TRANSFORMS)
                {
                    attribute = skeleton.GetRestTransformsAttr();
                }
                else
                {
                    WriteError(error, "The requested skeleton matrix property is unsupported.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }
                return GetSchemaArray<openusd_matrix4d, GfMatrix4d>(
                    attribute,
                    UsdTimeCode::Default(),
                    values,
                    capacity,
                    required,
                    SdfValueTypeNames->Matrix4dArray,
                    "skeleton matrices",
                    [](const GfMatrix4d& value) { return FromMatrix4d(value); },
                    error);
            });
        });

    });
}

openusd_status openusd_skel_set_animation_vec3(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_animation_vec3_property property,
    const openusd_vec3f* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!IsValidArrayBuffer(values, count) ||
            (time_sampled != 0 && !std::isfinite(time_code)))
        {
            WriteError(error, "A valid animation vector buffer and finite time code are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        for (size_t index = 0; index < count; ++index)
        {
            if (!std::isfinite(values[index].x) ||
                !std::isfinite(values[index].y) ||
                !std::isfinite(values[index].z))
            {
                WriteError(error, "Animation vectors must contain only finite values.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            if (property == OPENUSD_SKEL_ANIMATION_SCALES &&
                (std::abs(values[index].x) > 65504.0F ||
                 std::abs(values[index].y) > 65504.0F ||
                 std::abs(values[index].z) > 65504.0F))
            {
                WriteError(error, "Animation scales must fit the half3 schema representation.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
        }
        return Guard(error, [&]()
        {
            UsdSkelAnimation animation;
            openusd_status status = GetSkelAnimation(stage, prim_path, &animation, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            size_t jointCount = 0;
            status = GetAnimationJointCount(animation, &jointCount, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            if (count != jointCount)
            {
                WriteError(error, "Animation vector cardinality must match joints.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            if (property == OPENUSD_SKEL_ANIMATION_TRANSLATIONS)
            {
                return SetSchemaArray<openusd_vec3f, GfVec3f>(
                    animation.CreateTranslationsAttr(),
                    values,
                    count,
                    GetTimeCode(time_sampled, time_code),
                    SdfValueTypeNames->Float3Array,
                    "animation translations",
                    [](openusd_vec3f value) { return GfVec3f(value.x, value.y, value.z); },
                    error);
            }
            if (property == OPENUSD_SKEL_ANIMATION_SCALES)
            {
                return SetSchemaArray<openusd_vec3f, GfVec3h>(
                    animation.CreateScalesAttr(),
                    values,
                    count,
                    GetTimeCode(time_sampled, time_code),
                    SdfValueTypeNames->Half3Array,
                    "animation scales",
                    [](openusd_vec3f value)
                    {
                        return GfVec3h(value.x, value.y, value.z);
                    },
                    error);
            }
            WriteError(error, "The requested animation vector property is unsupported.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        });

    });
}

openusd_status openusd_skel_get_animation_vec3(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_animation_vec3_property property,
    int32_t time_sampled,
    double time_code,
    openusd_vec3f* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(required);
        return WithAbiWritableBuffer(values, capacity, [&]()
        {
            if (time_sampled != 0 && !std::isfinite(time_code))
            {
                WriteError(error, "A finite animation time code is required.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            return Guard(error, [&]()
            {
                UsdSkelAnimation animation;
                const openusd_status status =
                    GetSkelAnimation(stage, prim_path, &animation, error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                if (property == OPENUSD_SKEL_ANIMATION_TRANSLATIONS)
                {
                    return GetSchemaArray<openusd_vec3f, GfVec3f>(
                        animation.GetTranslationsAttr(),
                        GetTimeCode(time_sampled, time_code),
                        values,
                        capacity,
                        required,
                        SdfValueTypeNames->Float3Array,
                        "animation translations",
                        [](const GfVec3f& value)
                        {
                            return openusd_vec3f{value[0], value[1], value[2]};
                        },
                        error);
                }
                if (property == OPENUSD_SKEL_ANIMATION_SCALES)
                {
                    return GetSchemaArray<openusd_vec3f, GfVec3h>(
                        animation.GetScalesAttr(),
                        GetTimeCode(time_sampled, time_code),
                        values,
                        capacity,
                        required,
                        SdfValueTypeNames->Half3Array,
                        "animation scales",
                        [](const GfVec3h& value)
                        {
                            return openusd_vec3f{
                                static_cast<float>(value[0]),
                                static_cast<float>(value[1]),
                                static_cast<float>(value[2])};
                        },
                        error);
                }
                WriteError(error, "The requested animation vector property is unsupported.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            });
        });

    });
}

openusd_status openusd_skel_set_animation_rotations(
    const openusd_stage* stage,
    const char* prim_path,
    const openusd_quatf* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!IsValidArrayBuffer(values, count) ||
            (time_sampled != 0 && !std::isfinite(time_code)))
        {
            WriteError(error, "A valid quaternion buffer and finite time code are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        for (size_t index = 0; index < count; ++index)
        {
            const openusd_quatf& value = values[index];
            const float lengthSquared =
                (value.real * value.real) + (value.x * value.x) +
                (value.y * value.y) + (value.z * value.z);
            if (!std::isfinite(lengthSquared) || std::abs(lengthSquared - 1.0F) > 0.002F)
            {
                WriteError(error, "Animation rotations must be finite unit quaternions.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
        }
        return Guard(error, [&]()
        {
            UsdSkelAnimation animation;
            openusd_status status = GetSkelAnimation(stage, prim_path, &animation, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            size_t jointCount = 0;
            status = GetAnimationJointCount(animation, &jointCount, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            if (count != jointCount)
            {
                WriteError(error, "Animation rotation cardinality must match joints.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            return SetSchemaArray<openusd_quatf, GfQuatf>(
                animation.CreateRotationsAttr(),
                values,
                count,
                GetTimeCode(time_sampled, time_code),
                SdfValueTypeNames->QuatfArray,
                "animation rotations",
                [](openusd_quatf value)
                {
                    return GfQuatf(value.real, GfVec3f(value.x, value.y, value.z));
                },
                error);
        });

    });
}

openusd_status openusd_skel_get_animation_rotations(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t time_sampled,
    double time_code,
    openusd_quatf* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(required);
        return WithAbiWritableBuffer(values, capacity, [&]()
        {
            if (time_sampled != 0 && !std::isfinite(time_code))
            {
                WriteError(error, "A finite animation time code is required.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            return Guard(error, [&]()
            {
                UsdSkelAnimation animation;
                const openusd_status status =
                    GetSkelAnimation(stage, prim_path, &animation, error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                return GetSchemaArray<openusd_quatf, GfQuatf>(
                    animation.GetRotationsAttr(),
                    GetTimeCode(time_sampled, time_code),
                    values,
                    capacity,
                    required,
                    SdfValueTypeNames->QuatfArray,
                    "animation rotations",
                    [](const GfQuatf& value)
                    {
                        const GfVec3f imaginary = value.GetImaginary();
                        return openusd_quatf{
                            value.GetReal(), imaginary[0], imaginary[1], imaginary[2]};
                    },
                    error);
            });
        });

    });
}

openusd_status openusd_skel_set_binding_target(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_binding_relationship relationship,
    const char* target_prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!IsValidPrimPath(target_prim_path))
        {
            WriteError(error, "A valid absolute skeleton binding target path is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            UsdSkelBindingAPI binding;
            openusd_status status = GetSkelBinding(stage, prim_path, &prim, &binding, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            const SdfPath targetPath(target_prim_path);
            status = ValidateSkelBindingTarget(stage, relationship, targetPath, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            const UsdRelationship target =
                GetSkelBindingRelationship(binding, relationship, true, error);
            if (!target || !target.SetTargets(SdfPathVector{targetPath}))
            {
                WriteError(error, "Could not set the skeleton binding relationship target.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_skel_get_binding_target(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_binding_relationship relationship,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiStringOutput(buffer, capacity);
        ResetAbiOutput(required);
        if (required == nullptr)
        {
            WriteError(error, "A skeleton binding target size output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            UsdSkelBindingAPI binding;
            openusd_status status = GetSkelBinding(stage, prim_path, &prim, &binding, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            const UsdRelationship target =
                GetSkelBindingRelationship(binding, relationship, false, error);
            if (!target)
            {
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            SdfPathVector targets;
            if (!target.GetTargets(&targets) || targets.empty())
            {
                WriteError(error, "The skeleton binding relationship has no target.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (targets.size() != 1)
            {
                WriteError(error, "The skeleton binding relationship must have exactly one target.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            status = ValidateSkelBindingTarget(stage, relationship, targets[0], error);
            return status == OPENUSD_STATUS_OK
                ? CopyString(targets[0].GetString(), buffer, capacity, required)
                : status;
        });

    });
}

openusd_status openusd_skel_clear_binding_target(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_binding_relationship relationship,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        return Guard(error, [&]()
        {
            UsdPrim prim;
            UsdSkelBindingAPI binding;
            const openusd_status status = GetSkelBinding(stage, prim_path, &prim, &binding, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            const UsdRelationship target =
                GetSkelBindingRelationship(binding, relationship, false, error);
            if (!target)
            {
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            if (!target.ClearTargets(true))
            {
                WriteError(error, "Could not clear the skeleton binding relationship.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_skel_set_geom_bind_transform(
    const openusd_stage* stage,
    const char* prim_path,
    const openusd_matrix4d* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (value == nullptr || !IsAligned(value) || !IsFiniteMatrix(*value))
        {
            WriteError(error, "An aligned finite geometry bind transform is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            UsdSkelBindingAPI binding;
            const openusd_status status = GetSkelBinding(stage, prim_path, &prim, &binding, error);
            return status == OPENUSD_STATUS_OK
                ? SetLuxAttribute(
                    binding.CreateGeomBindTransformAttr(),
                    ToMatrix4d(*value),
                    "geometry bind transform",
                    error)
                : status;
        });

    });
}

openusd_status openusd_skel_get_geom_bind_transform(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_matrix4d* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (value == nullptr || !IsAligned(value))
        {
            WriteError(error, "An aligned geometry bind transform output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            UsdSkelBindingAPI binding;
            const openusd_status status = GetSkelBinding(stage, prim_path, &prim, &binding, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            GfMatrix4d matrix;
            const openusd_status readStatus = GetLuxAttribute(
                binding.GetGeomBindTransformAttr(), &matrix, "geometry bind transform", error);
            if (readStatus == OPENUSD_STATUS_OK)
            {
                *value = FromMatrix4d(matrix);
            }
            return readStatus;
        });

    });
}

openusd_status openusd_skel_set_joint_influences(
    const openusd_stage* stage,
    const char* prim_path,
    const int32_t* joint_indices,
    size_t joint_index_count,
    const float* joint_weights,
    size_t joint_weight_count,
    int32_t element_size,
    openusd_skel_interpolation interpolation,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!IsValidArrayBuffer(joint_indices, joint_index_count) ||
            !IsValidArrayBuffer(joint_weights, joint_weight_count) ||
            joint_index_count == 0 || joint_index_count != joint_weight_count ||
            element_size <= 0 ||
            joint_index_count % static_cast<size_t>(element_size) != 0 ||
            (interpolation != OPENUSD_SKEL_INTERPOLATION_CONSTANT &&
             interpolation != OPENUSD_SKEL_INTERPOLATION_VERTEX) ||
            (interpolation == OPENUSD_SKEL_INTERPOLATION_CONSTANT &&
             joint_index_count != static_cast<size_t>(element_size)))
        {
            WriteError(
                error,
                "Joint indices and weights must have equal non-zero tuple-shaped cardinality.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        for (size_t index = 0; index < joint_weight_count; ++index)
        {
            if (!std::isfinite(joint_weights[index]) || joint_weights[index] < 0.0F)
            {
                WriteError(error, "Joint weights must be finite and non-negative.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
        }
        return Guard(error, [&]()
        {
            UsdPrim prim;
            UsdSkelBindingAPI binding;
            openusd_status status = GetSkelBinding(stage, prim_path, &prim, &binding, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            size_t jointCount = 0;
            status = GetBoundSkeletonJointCount(binding, &jointCount, error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            std::vector<int> indices(joint_index_count);
            for (size_t index = 0; index < joint_index_count; ++index)
            {
                indices[index] = joint_indices[index];
            }
            std::string reason;
            if (!UsdSkelBindingAPI::ValidateJointIndices(
                    TfSpan<const int>(indices.data(), indices.size()), jointCount, &reason))
            {
                WriteError(error, reason.empty() ? "Joint indices are invalid." : reason);
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            const bool constant = interpolation == OPENUSD_SKEL_INTERPOLATION_CONSTANT;
            const UsdGeomPrimvar indicesPrimvar =
                binding.CreateJointIndicesPrimvar(constant, element_size);
            const UsdGeomPrimvar weightsPrimvar =
                binding.CreateJointWeightsPrimvar(constant, element_size);
            if (!indicesPrimvar || !weightsPrimvar)
            {
                WriteError(error, "Could not create skeleton influence primvars.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            VtIntArray indexValues(indices.begin(), indices.end());
            VtFloatArray weightValues(joint_weights, joint_weights + joint_weight_count);
            if (!indicesPrimvar.GetAttr().Set(indexValues) ||
                !weightsPrimvar.GetAttr().Set(weightValues))
            {
                WriteError(error, "Could not set skeleton influence primvars.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_skel_get_joint_influences(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* joint_indices,
    size_t joint_index_capacity,
    size_t* joint_index_required,
    float* joint_weights,
    size_t joint_weight_capacity,
    size_t* joint_weight_required,
    int32_t* element_size,
    openusd_skel_interpolation* interpolation,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(joint_index_required);
        ResetAbiOutput(joint_weight_required);
        ResetAbiOutput(element_size);
        ResetAbiOutput(interpolation);
        return WithAbiWritableBuffers(
            joint_indices,
            joint_index_capacity,
            joint_weights,
            joint_weight_capacity,
            [&]()
        {
            if (joint_index_required == nullptr || joint_weight_required == nullptr ||
                element_size == nullptr || interpolation == nullptr ||
                !IsValidArrayBuffer(joint_indices, joint_index_capacity) ||
                !IsValidArrayBuffer(joint_weights, joint_weight_capacity))
            {
                WriteError(error, "Valid influence buffers and metadata outputs are required.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            return Guard(error, [&]()
            {
                UsdPrim prim;
                UsdSkelBindingAPI binding;
                openusd_status status =
                    GetSkelBinding(stage, prim_path, &prim, &binding, error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                const UsdGeomPrimvar indicesPrimvar = binding.GetJointIndicesPrimvar();
                const UsdGeomPrimvar weightsPrimvar = binding.GetJointWeightsPrimvar();
                if (!indicesPrimvar || !weightsPrimvar)
                {
                    WriteError(error, "The prim has no authored joint influence primvars.");
                    return OPENUSD_STATUS_NOT_FOUND;
                }

                VtIntArray indexValues;
                VtFloatArray weightValues;
                if (!indicesPrimvar.GetAttr().Get(&indexValues) ||
                    !weightsPrimvar.GetAttr().Get(&weightValues))
                {
                    WriteError(error, "Could not read the joint influence primvars.");
                    return OPENUSD_STATUS_NOT_FOUND;
                }
                const int indexElementSize = indicesPrimvar.GetElementSize();
                const int weightElementSize = weightsPrimvar.GetElementSize();
                const TfToken indexInterpolation = indicesPrimvar.GetInterpolation();
                const TfToken weightInterpolation = weightsPrimvar.GetInterpolation();
                if (indexValues.empty() || indexValues.size() != weightValues.size() ||
                    indexElementSize <= 0 || indexElementSize != weightElementSize ||
                    indexValues.size() % static_cast<size_t>(indexElementSize) != 0 ||
                    indexInterpolation != weightInterpolation)
                {
                    WriteError(error, "The joint influence primvars have inconsistent shape.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }

                openusd_skel_interpolation outputInterpolation;
                if (indexInterpolation == UsdGeomTokens->constant)
                {
                    outputInterpolation = OPENUSD_SKEL_INTERPOLATION_CONSTANT;
                    if (indexValues.size() != static_cast<size_t>(indexElementSize))
                    {
                        WriteError(error, "Constant joint influences must contain one tuple.");
                        return OPENUSD_STATUS_INVALID_ARGUMENT;
                    }
                }
                else if (indexInterpolation == UsdGeomTokens->vertex)
                {
                    outputInterpolation = OPENUSD_SKEL_INTERPOLATION_VERTEX;
                }
                else
                {
                    WriteError(error, "Joint influences must use constant or vertex interpolation.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }

                size_t jointCount = 0;
                status = GetBoundSkeletonJointCount(binding, &jointCount, error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                std::string reason;
                if (!UsdSkelBindingAPI::ValidateJointIndices(
                        TfSpan<const int>(indexValues.data(), indexValues.size()),
                        jointCount,
                        &reason))
                {
                    WriteError(error, reason.empty() ? "Joint indices are invalid." : reason);
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }
                for (float weight : weightValues)
                {
                    if (!std::isfinite(weight) || weight < 0.0F)
                    {
                        WriteError(error, "Joint weights must be finite and non-negative.");
                        return OPENUSD_STATUS_INVALID_ARGUMENT;
                    }
                }

                const size_t indexCount = indexValues.size();
                const size_t weightCount = weightValues.size();
                if (joint_indices == nullptr && joint_index_capacity == 0 &&
                    joint_weights == nullptr && joint_weight_capacity == 0)
                {
                    *joint_index_required = indexCount;
                    *joint_weight_required = weightCount;
                    *element_size = indexElementSize;
                    *interpolation = outputInterpolation;
                    return OPENUSD_STATUS_OK;
                }
                if (joint_indices == nullptr || joint_weights == nullptr ||
                    joint_index_capacity < indexCount ||
                    joint_weight_capacity < weightCount)
                {
                    return OPENUSD_STATUS_BUFFER_TOO_SMALL;
                }
                for (size_t index = 0; index < indexCount; ++index)
                {
                    joint_indices[index] = indexValues[index];
                    joint_weights[index] = weightValues[index];
                }
                *joint_index_required = indexCount;
                *joint_weight_required = weightCount;
                *element_size = indexElementSize;
                *interpolation = outputInterpolation;
                return OPENUSD_STATUS_OK;
            });
        });

    });
}
