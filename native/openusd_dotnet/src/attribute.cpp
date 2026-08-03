// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

openusd_status openusd_stage_get_attribute_type_name(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
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
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0')
        {
            WriteError(error, "A valid stage, prim path, and attribute name are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdAttribute attribute =
                prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            return CopyString(attribute.GetTypeName().GetAsToken().GetString(), buffer, capacity, required);
        });

    });
}

openusd_status openusd_stage_get_attribute_value_state(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    int32_t* has_authored_value_opinion,
    int32_t* value_is_blocked,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(has_authored_value_opinion);
        ResetAbiOutput(value_is_blocked);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0' ||
            has_authored_value_opinion == nullptr || value_is_blocked == nullptr)
        {
            WriteError(error, "A valid stage, prim path, attribute name, and state outputs are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdAttribute attribute =
                prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const UsdResolveInfo resolveInfo =
                attribute.GetResolveInfo(GetTimeCode(time_sampled, time_code));
            *has_authored_value_opinion = attribute.HasAuthoredValueOpinion() ? 1 : 0;
            *value_is_blocked = resolveInfo.ValueIsBlocked() ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_attribute_time_samples(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    double* values,
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
            if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
                attribute_name == nullptr || attribute_name[0] == '\0' || required == nullptr ||
                !IsValidArrayBuffer(values, capacity))
            {
                WriteError(
                    error,
                    "A valid stage, prim path, attribute name, aligned buffer, and size output are required.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            return Guard(error, [&]()
            {
                const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
                const UsdAttribute attribute =
                    prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
                if (!attribute)
                {
                    WriteError(error, "The requested attribute was not found.");
                    return OPENUSD_STATUS_NOT_FOUND;
                }

                TfErrorMark mark;
                std::vector<double> samples;
                const bool read = attribute.GetTimeSamples(&samples);
                if (!read || !mark.IsClean())
                {
                    const bool had_errors = !mark.IsClean();
                    std::string message = ConsumeErrors(mark);
                    WriteError(
                        error,
                        message.empty() ? "Could not read attribute time samples." : message);
                    return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
                }

                const size_t count = samples.size();
                if (values == nullptr && capacity == 0)
                {
                    *required = count;
                    return OPENUSD_STATUS_OK;
                }
                if (count == 0)
                {
                    *required = 0;
                    return OPENUSD_STATUS_OK;
                }
                if (values == nullptr || capacity < count)
                {
                    return OPENUSD_STATUS_BUFFER_TOO_SMALL;
                }
                std::copy(samples.begin(), samples.end(), values);
                *required = count;
                return OPENUSD_STATUS_OK;
            });
        });

    });
}

openusd_status openusd_stage_clear_attribute_value(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0')
        {
            WriteError(error, "A valid stage, prim path, and attribute name are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdAttribute attribute =
                prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            TfErrorMark mark;
            const bool cleared = attribute.Clear();
            if (!cleared || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not clear the attribute value." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_block_attribute_value(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0')
        {
            WriteError(error, "A valid stage, prim path, and attribute name are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdAttribute attribute =
                prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            TfErrorMark mark;
            attribute.Block();
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not block the attribute value." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_attribute_scalar_value(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    openusd_scalar_value* value,
    char* string_buffer,
    size_t string_capacity,
    size_t* string_required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetVersionedAbiOutput(value);
        ResetAbiStringOutput(string_buffer, string_capacity);
        ResetAbiOutput(string_required);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0' ||
            value == nullptr ||
            value->struct_size < offsetof(openusd_scalar_value, matrix4d_value) ||
            string_required == nullptr)
        {
            WriteError(error, "A valid stage, attribute, versioned value, and string size output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        *string_required = 0;
        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdAttribute attribute =
                prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const UsdTimeCode time = GetTimeCode(time_sampled, time_code);
            const SdfValueTypeName typeName = attribute.GetTypeName();
            TfErrorMark mark;
            bool read = false;

            if (typeName == SdfValueTypeNames->Bool)
            {
                bool result = false;
                read = attribute.Get(&result, time);
                value->kind = OPENUSD_SCALAR_KIND_BOOL;
                value->bool_value = result ? 1 : 0;
            }
            else if (typeName == SdfValueTypeNames->Int64)
            {
                read = attribute.Get(&value->int64_value, time);
                value->kind = OPENUSD_SCALAR_KIND_INT64;
            }
            else if (typeName == SdfValueTypeNames->Double)
            {
                read = attribute.Get(&value->double_value, time);
                value->kind = OPENUSD_SCALAR_KIND_DOUBLE;
            }
            else if (typeName == SdfValueTypeNames->String)
            {
                std::string result;
                read = attribute.Get(&result, time);
                value->kind = OPENUSD_SCALAR_KIND_STRING;
                if (read && mark.IsClean())
                {
                    return CopyString(result, string_buffer, string_capacity, string_required);
                }
            }
            else if (typeName == SdfValueTypeNames->Token)
            {
                TfToken result;
                read = attribute.Get(&result, time);
                value->kind = OPENUSD_SCALAR_KIND_TOKEN;
                if (read && mark.IsClean())
                {
                    return CopyString(result.GetString(), string_buffer, string_capacity, string_required);
                }
            }
            else if (typeName == SdfValueTypeNames->Float3 ||
                     typeName == SdfValueTypeNames->Vector3f ||
                     typeName == SdfValueTypeNames->Color3f)
            {
                GfVec3f result;
                read = attribute.Get(&result, time);
                value->kind = typeName == SdfValueTypeNames->Color3f
                    ? OPENUSD_SCALAR_KIND_COLOR3F
                    : OPENUSD_SCALAR_KIND_VEC3F;
                value->vec3f_value = {result[0], result[1], result[2]};
            }
            else if (typeName == SdfValueTypeNames->Matrix4d)
            {
                if (value->struct_size < sizeof(openusd_scalar_value))
                {
                    WriteError(error, "The tagged scalar value is too small for a matrix4d payload.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }
                GfMatrix4d result;
                read = attribute.Get(&result, time);
                value->kind = OPENUSD_SCALAR_KIND_MATRIX4D;
                for (int row = 0; row < 4; ++row)
                {
                    for (int column = 0; column < 4; ++column)
                    {
                        value->matrix4d_value.values[(row * 4) + column] = result[row][column];
                    }
                }
            }
            else
            {
                WriteError(
                    error,
                    std::string("The attribute type is not a supported scalar: ") +
                        typeName.GetAsToken().GetString());
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            if (!read || !mark.IsClean())
            {
                const bool hadErrors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                if (message.empty())
                {
                    message = attribute.GetResolveInfo(time).ValueIsBlocked()
                        ? "The attribute value is blocked."
                        : "The attribute has no readable scalar value.";
                }
                WriteError(error, message);
                return hadErrors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_double(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    double value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0')
        {
            WriteError(error, "A valid stage, prim path, and attribute name are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const TfToken name(attribute_name);
            UsdAttribute attribute = prim.GetAttribute(name);
            if (!attribute)
            {
                attribute = prim.CreateAttribute(name, SdfValueTypeNames->Double, true);
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Double, "double", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }
            const bool set = attribute && attribute.Set(value, GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the double attribute." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_double(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    double* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || value == nullptr)
        {
            WriteError(error, "A valid stage, prim path, attribute name, and output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdAttribute attribute = prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested double attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Double, "double", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }

            TfErrorMark mark;
            const bool read = attribute.Get(value, GetTimeCode(time_sampled, time_code));
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read the double attribute." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_double_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const double* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || (values == nullptr && count != 0))
        {
            WriteError(error, "A valid stage, prim path, attribute name, and value buffer are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            TfErrorMark mark;
            const TfToken name(attribute_name);
            UsdAttribute attribute = prim.GetAttribute(name);
            if (!attribute)
            {
                attribute = prim.CreateAttribute(name, SdfValueTypeNames->DoubleArray, true);
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->DoubleArray, "double array", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }

            VtArray<double> array(count);
            if (count != 0)
            {
                std::copy(values, values + count, array.begin());
            }
            const bool set = attribute && attribute.Set(array, GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the double array attribute." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_double_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    double* values,
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
            if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
                attribute_name == nullptr || attribute_name[0] == '\0' || required == nullptr ||
                !IsValidArrayBuffer(values, capacity))
            {
                WriteError(
                    error,
                    "A valid stage, prim path, attribute name, aligned buffer, and size output are required.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            return Guard(error, [&]()
            {
                const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
                const UsdAttribute attribute =
                    prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
                if (!attribute)
                {
                    WriteError(error, "The requested double array attribute was not found.");
                    return OPENUSD_STATUS_NOT_FOUND;
                }
                const openusd_status typeStatus = ValidateAttributeType(
                    attribute, SdfValueTypeNames->DoubleArray, "double array", error);
                if (typeStatus != OPENUSD_STATUS_OK)
                {
                    return typeStatus;
                }

                TfErrorMark mark;
                VtArray<double> array;
                const bool read = attribute.Get(&array, GetTimeCode(time_sampled, time_code));
                if (!read || !mark.IsClean())
                {
                    const bool had_errors = !mark.IsClean();
                    std::string message = ConsumeErrors(mark);
                    WriteError(
                        error,
                        message.empty() ? "Could not read the double array attribute." : message);
                    return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
                }

                const size_t count = array.size();
                if (values == nullptr && capacity == 0)
                {
                    *required = count;
                    return OPENUSD_STATUS_OK;
                }
                if (count == 0)
                {
                    *required = 0;
                    return OPENUSD_STATUS_OK;
                }
                if (values == nullptr || capacity < count)
                {
                    return OPENUSD_STATUS_BUFFER_TOO_SMALL;
                }
                std::copy(array.begin(), array.end(), values);
                *required = count;
                return OPENUSD_STATUS_OK;
            });
        });

    });
}

openusd_status openusd_stage_set_matrix4d(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const openusd_matrix4d* value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0' ||
            value == nullptr || !IsAligned(value))
        {
            WriteError(error, "A valid stage, prim path, attribute name, and aligned matrix are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            TfErrorMark mark;
            const TfToken name(attribute_name);
            UsdAttribute attribute = prim.GetAttribute(name);
            if (attribute && attribute.GetTypeName() != SdfValueTypeNames->Matrix4d)
            {
                WriteError(error, "The attribute is not a matrix4d.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            if (!attribute)
            {
                attribute = prim.CreateAttribute(name, SdfValueTypeNames->Matrix4d, true);
            }

            GfMatrix4d matrix(0.0);
            for (int row = 0; row < 4; ++row)
            {
                for (int column = 0; column < 4; ++column)
                {
                    matrix[row][column] = value->values[(row * 4) + column];
                }
            }
            const bool set = attribute && attribute.Set(matrix, GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the matrix4d attribute." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_matrix4d(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    openusd_matrix4d* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0' ||
            value == nullptr || !IsAligned(value))
        {
            WriteError(error, "A valid stage, prim path, attribute name, and aligned matrix output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdAttribute attribute =
                prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested matrix4d attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (attribute.GetTypeName() != SdfValueTypeNames->Matrix4d)
            {
                WriteError(error, "The attribute is not a matrix4d.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            TfErrorMark mark;
            const UsdTimeCode time = GetTimeCode(time_sampled, time_code);
            GfMatrix4d matrix;
            const bool read = attribute.Get(&matrix, time);
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                if (message.empty())
                {
                    message = attribute.GetResolveInfo(time).ValueIsBlocked()
                        ? "The attribute value is blocked."
                        : "The attribute has no readable matrix4d value.";
                }
                WriteError(error, message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }

            for (int row = 0; row < 4; ++row)
            {
                for (int column = 0; column < 4; ++column)
                {
                    value->values[(row * 4) + column] = matrix[row][column];
                }
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_int32_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const int32_t* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0' ||
            !IsValidArrayBuffer(values, count))
        {
            WriteError(error, "A valid stage, prim path, attribute name, aligned buffer, and count are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            TfErrorMark mark;
            const TfToken name(attribute_name);
            UsdAttribute attribute = prim.GetAttribute(name);
            SdfValueTypeName typeName = SdfValueTypeNames->IntArray;
            if (attribute)
            {
                typeName = attribute.GetTypeName();
                if (typeName != SdfValueTypeNames->IntArray &&
                    typeName != SdfValueTypeNames->Int3Array &&
                    typeName != SdfValueTypeNames->Int4Array)
                {
                    WriteError(error, "The attribute is not a int32 array.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }
            }
            else
            {
                attribute = prim.CreateAttribute(name, typeName, true);
            }

            bool set = false;
            if (typeName == SdfValueTypeNames->IntArray)
            {
                VtArray<int> array(count);
                for (size_t index = 0; index < count; ++index)
                {
                    array[index] = static_cast<int>(values[index]);
                }
                set = attribute && attribute.Set(array, GetTimeCode(time_sampled, time_code));
            }
            else if (typeName == SdfValueTypeNames->Int3Array)
            {
                if (count % 3 != 0)
                {
                    WriteError(error, "The int3 array requires a count divisible by three.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }
                VtArray<GfVec3i> array(count / 3);
                for (size_t index = 0; index < array.size(); ++index)
                {
                    const size_t valueIndex = index * 3;
                    array[index] = GfVec3i(
                        values[valueIndex],
                        values[valueIndex + 1],
                        values[valueIndex + 2]);
                }
                set = attribute && attribute.Set(array, GetTimeCode(time_sampled, time_code));
            }
            else
            {
                if (count % 4 != 0)
                {
                    WriteError(error, "The int4 array requires a count divisible by four.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }
                VtArray<GfVec4i> array(count / 4);
                for (size_t index = 0; index < array.size(); ++index)
                {
                    const size_t valueIndex = index * 4;
                    array[index] = GfVec4i(
                        values[valueIndex],
                        values[valueIndex + 1],
                        values[valueIndex + 2],
                        values[valueIndex + 3]);
                }
                set = attribute && attribute.Set(array, GetTimeCode(time_sampled, time_code));
            }
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the int32 array attribute." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_int32_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    int32_t* values,
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
            if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
                attribute_name == nullptr || attribute_name[0] == '\0' ||
                required == nullptr || !IsValidArrayBuffer(values, capacity))
            {
                WriteError(error, "A valid stage, prim path, attribute name, aligned buffer, and size output are required.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            return Guard(error, [&]()
            {
                const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
                const UsdAttribute attribute =
                    prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
                if (!attribute)
                {
                    WriteError(error, "The requested int32 array was not found.");
                    return OPENUSD_STATUS_NOT_FOUND;
                }

                const SdfValueTypeName typeName = attribute.GetTypeName();
                const UsdTimeCode time = GetTimeCode(time_sampled, time_code);
                TfErrorMark mark;
                if (typeName == SdfValueTypeNames->IntArray)
                {
                    VtArray<int> array;
                    const bool read = attribute.Get(&array, time);
                    if (!read || !mark.IsClean())
                    {
                        const bool hadErrors = !mark.IsClean();
                        std::string message = ConsumeErrors(mark);
                        if (message.empty())
                        {
                            message = attribute.GetResolveInfo(time).ValueIsBlocked()
                                ? "The attribute value is blocked."
                                : "The attribute has no readable int32 array value.";
                        }
                        WriteError(error, message);
                        return hadErrors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
                    }
                    if (values == nullptr && capacity == 0)
                    {
                        *required = array.size();
                        return OPENUSD_STATUS_OK;
                    }
                    if (capacity < array.size())
                    {
                        WriteError(error, "The supplied int32 array buffer is too small.");
                        return OPENUSD_STATUS_BUFFER_TOO_SMALL;
                    }
                    for (size_t index = 0; index < array.size(); ++index)
                    {
                        values[index] = static_cast<int32_t>(array[index]);
                    }
                    *required = array.size();
                    return OPENUSD_STATUS_OK;
                }
                if (typeName == SdfValueTypeNames->Int3Array)
                {
                    VtArray<GfVec3i> array;
                    const bool read = attribute.Get(&array, time);
                    if (!read || !mark.IsClean())
                    {
                        const bool hadErrors = !mark.IsClean();
                        std::string message = ConsumeErrors(mark);
                        if (message.empty())
                        {
                            message = attribute.GetResolveInfo(time).ValueIsBlocked()
                                ? "The attribute value is blocked."
                                : "The attribute has no readable int3 array value.";
                        }
                        WriteError(error, message);
                        return hadErrors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
                    }
                    const size_t requiredCount = array.size() * 3;
                    if (values == nullptr && capacity == 0)
                    {
                        *required = requiredCount;
                        return OPENUSD_STATUS_OK;
                    }
                    if (capacity < requiredCount)
                    {
                        WriteError(error, "The supplied int32 array buffer is too small.");
                        return OPENUSD_STATUS_BUFFER_TOO_SMALL;
                    }
                    for (size_t index = 0; index < array.size(); ++index)
                    {
                        const size_t valueIndex = index * 3;
                        values[valueIndex] = array[index][0];
                        values[valueIndex + 1] = array[index][1];
                        values[valueIndex + 2] = array[index][2];
                    }
                    *required = requiredCount;
                    return OPENUSD_STATUS_OK;
                }
                if (typeName == SdfValueTypeNames->Int4Array)
                {
                    VtArray<GfVec4i> array;
                    const bool read = attribute.Get(&array, time);
                    if (!read || !mark.IsClean())
                    {
                        const bool hadErrors = !mark.IsClean();
                        std::string message = ConsumeErrors(mark);
                        if (message.empty())
                        {
                            message = attribute.GetResolveInfo(time).ValueIsBlocked()
                                ? "The attribute value is blocked."
                                : "The attribute has no readable int4 array value.";
                        }
                        WriteError(error, message);
                        return hadErrors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
                    }
                    const size_t requiredCount = array.size() * 4;
                    if (values == nullptr && capacity == 0)
                    {
                        *required = requiredCount;
                        return OPENUSD_STATUS_OK;
                    }
                    if (capacity < requiredCount)
                    {
                        WriteError(error, "The supplied int32 array buffer is too small.");
                        return OPENUSD_STATUS_BUFFER_TOO_SMALL;
                    }
                    for (size_t index = 0; index < array.size(); ++index)
                    {
                        const size_t valueIndex = index * 4;
                        values[valueIndex] = array[index][0];
                        values[valueIndex + 1] = array[index][1];
                        values[valueIndex + 2] = array[index][2];
                        values[valueIndex + 3] = array[index][3];
                    }
                    *required = requiredCount;
                    return OPENUSD_STATUS_OK;
                }

                WriteError(error, "The attribute is not a int32 array.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            });
        });

    });
}

openusd_status openusd_stage_set_float_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const float* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        return SetArrayAttribute<float, float>(
            stage,
            prim_path,
            attribute_name,
            values,
            count,
            time_sampled,
            time_code,
            SdfValueTypeNames->FloatArray,
            "float",
            [](const SdfValueTypeName& type) { return type == SdfValueTypeNames->FloatArray; },
            [](float value) { return value; },
            error);

    });
}

openusd_status openusd_stage_get_float_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    float* values,
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
            return GetArrayAttribute<float, float>(
                stage,
                prim_path,
                attribute_name,
                time_sampled,
                time_code,
                values,
                capacity,
                required,
                "float",
                [](const SdfValueTypeName& type) { return type == SdfValueTypeNames->FloatArray; },
                [](float value) { return value; },
                error);
        });

    });
}

openusd_status openusd_stage_set_vec2f_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const openusd_vec2f* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        return SetArrayAttribute<openusd_vec2f, GfVec2f>(
            stage,
            prim_path,
            attribute_name,
            values,
            count,
            time_sampled,
            time_code,
            SdfValueTypeNames->Float2Array,
            "vec2f",
            [](const SdfValueTypeName& type)
            {
                return type == SdfValueTypeNames->Float2Array ||
                    type == SdfValueTypeNames->TexCoord2fArray;
            },
            [](const openusd_vec2f& value) { return GfVec2f(value.x, value.y); },
            error);

    });
}

openusd_status openusd_stage_get_vec2f_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    openusd_vec2f* values,
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
            return GetArrayAttribute<openusd_vec2f, GfVec2f>(
                stage,
                prim_path,
                attribute_name,
                time_sampled,
                time_code,
                values,
                capacity,
                required,
                "vec2f",
                [](const SdfValueTypeName& type)
                {
                    return type == SdfValueTypeNames->Float2Array ||
                        type == SdfValueTypeNames->TexCoord2fArray;
                },
                [](const GfVec2f& value) { return openusd_vec2f{value[0], value[1]}; },
                error);
        });

    });
}

openusd_status openusd_stage_set_vec3f_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const openusd_vec3f* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        return SetArrayAttribute<openusd_vec3f, GfVec3f>(
            stage,
            prim_path,
            attribute_name,
            values,
            count,
            time_sampled,
            time_code,
            SdfValueTypeNames->Float3Array,
            "vec3f",
            [](const SdfValueTypeName& type)
            {
                return type == SdfValueTypeNames->Float3Array ||
                    type == SdfValueTypeNames->Vector3fArray ||
                    type == SdfValueTypeNames->Point3fArray ||
                    type == SdfValueTypeNames->Normal3fArray ||
                    type == SdfValueTypeNames->Color3fArray;
            },
            [](const openusd_vec3f& value) { return GfVec3f(value.x, value.y, value.z); },
            error);

    });
}

openusd_status openusd_stage_get_vec3f_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
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
            return GetArrayAttribute<openusd_vec3f, GfVec3f>(
                stage,
                prim_path,
                attribute_name,
                time_sampled,
                time_code,
                values,
                capacity,
                required,
                "vec3f",
                [](const SdfValueTypeName& type)
                {
                    return type == SdfValueTypeNames->Float3Array ||
                        type == SdfValueTypeNames->Vector3fArray ||
                        type == SdfValueTypeNames->Point3fArray ||
                        type == SdfValueTypeNames->Normal3fArray ||
                        type == SdfValueTypeNames->Color3fArray;
                },
                [](const GfVec3f& value)
                {
                    return openusd_vec3f{value[0], value[1], value[2]};
                },
                error);
        });

    });
}

openusd_status openusd_stage_set_bool(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0')
        {
            WriteError(error, "A valid stage, prim path, and attribute name are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const TfToken name(attribute_name);
            UsdAttribute attribute = prim.GetAttribute(name);
            if (!attribute)
            {
                attribute = prim.CreateAttribute(name, SdfValueTypeNames->Bool, true);
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Bool, "bool", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }
            const bool set = attribute && attribute.Set(value != 0, GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the bool attribute." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_bool(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    int32_t* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || value == nullptr)
        {
            WriteError(error, "A valid stage, prim path, attribute name, and output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdAttribute attribute = prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested bool attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Bool, "bool", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }

            TfErrorMark mark;
            bool result = false;
            const bool read = attribute.Get(&result, GetTimeCode(time_sampled, time_code));
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read the bool attribute." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            *value = result ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_int64(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int64_t value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0')
        {
            WriteError(error, "A valid stage, prim path, and attribute name are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const TfToken name(attribute_name);
            UsdAttribute attribute = prim.GetAttribute(name);
            if (!attribute)
            {
                attribute = prim.CreateAttribute(name, SdfValueTypeNames->Int64, true);
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Int64, "int64", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }
            const bool set = attribute && attribute.Set(value, GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the int64 attribute." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_int64(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    int64_t* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || value == nullptr)
        {
            WriteError(error, "A valid stage, prim path, attribute name, and output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdAttribute attribute = prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested int64 attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Int64, "int64", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }

            TfErrorMark mark;
            const bool read = attribute.Get(value, GetTimeCode(time_sampled, time_code));
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read the int64 attribute." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_string(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const char* value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0' || value == nullptr)
        {
            WriteError(error, "A valid stage, prim path, attribute name, and value are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const TfToken name(attribute_name);
            UsdAttribute attribute = prim.GetAttribute(name);
            if (!attribute)
            {
                attribute = prim.CreateAttribute(name, SdfValueTypeNames->String, true);
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->String, "string", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }
            const bool set =
                attribute && attribute.Set(std::string(value), GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the string attribute." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_string(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
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
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || required == nullptr)
        {
            WriteError(error, "A valid stage, prim path, attribute name, and size output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdAttribute attribute = prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested string attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->String, "string", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }

            TfErrorMark mark;
            std::string result;
            const bool read = attribute.Get(&result, GetTimeCode(time_sampled, time_code));
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read the string attribute." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            return CopyString(result, buffer, capacity, required);
        });

    });
}

openusd_status openusd_stage_set_token(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const char* value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0' || value == nullptr)
        {
            WriteError(error, "A valid stage, prim path, attribute name, and value are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const TfToken name(attribute_name);
            UsdAttribute attribute = prim.GetAttribute(name);
            if (!attribute)
            {
                attribute = prim.CreateAttribute(name, SdfValueTypeNames->Token, true);
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Token, "token", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }
            const bool set =
                attribute && attribute.Set(TfToken(value), GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the token attribute." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_token(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
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
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || required == nullptr)
        {
            WriteError(error, "A valid stage, prim path, attribute name, and size output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdAttribute attribute = prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested token attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Token, "token", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }

            TfErrorMark mark;
            TfToken result;
            const bool read = attribute.Get(&result, GetTimeCode(time_sampled, time_code));
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read the token attribute." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            return CopyString(result.GetString(), buffer, capacity, required);
        });

    });
}

openusd_status openusd_stage_set_vec3f(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const openusd_vec3f* value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0' || value == nullptr)
        {
            WriteError(error, "A valid stage, prim path, attribute name, and value are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const TfToken name(attribute_name);
            UsdAttribute attribute = prim.GetAttribute(name);
            if (!attribute)
            {
                attribute = prim.CreateAttribute(name, SdfValueTypeNames->Float3, true);
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Float3, "float3", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }
            const GfVec3f vector(value->x, value->y, value->z);
            const bool set = attribute && attribute.Set(vector, GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the vec3f attribute." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_vec3f(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    openusd_vec3f* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || value == nullptr)
        {
            WriteError(error, "A valid stage, prim path, attribute name, and output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdAttribute attribute = prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested vec3f attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Float3, "float3", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }

            TfErrorMark mark;
            GfVec3f result;
            const bool read = attribute.Get(&result, GetTimeCode(time_sampled, time_code));
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read the vec3f attribute." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            value->x = result[0];
            value->y = result[1];
            value->z = result[2];
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_color3f(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const openusd_vec3f* value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || attribute_name[0] == '\0' || value == nullptr)
        {
            WriteError(error, "A valid stage, prim path, attribute name, and value are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const TfToken name(attribute_name);
            UsdAttribute attribute = prim.GetAttribute(name);
            if (!attribute)
            {
                attribute = prim.CreateAttribute(name, SdfValueTypeNames->Color3f, true);
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Color3f, "color3f", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }
            const GfVec3f vector(value->x, value->y, value->z);
            const bool set = attribute && attribute.Set(vector, GetTimeCode(time_sampled, time_code));
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the color3f attribute." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_color3f(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    openusd_vec3f* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            attribute_name == nullptr || value == nullptr)
        {
            WriteError(error, "A valid stage, prim path, attribute name, and output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            const UsdAttribute attribute = prim ? prim.GetAttribute(TfToken(attribute_name)) : UsdAttribute();
            if (!attribute)
            {
                WriteError(error, "The requested color3f attribute was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
        const openusd_status typeStatus =
            ValidateAttributeType(attribute, SdfValueTypeNames->Color3f, "color3f", error);
        if (typeStatus != OPENUSD_STATUS_OK)
        {
            return typeStatus;
        }

            TfErrorMark mark;
            GfVec3f result;
            const bool read = attribute.Get(&result, GetTimeCode(time_sampled, time_code));
            if (!read || !mark.IsClean())
            {
                const bool had_errors = !mark.IsClean();
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not read the color3f attribute." : message);
                return had_errors ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            value->x = result[0];
            value->y = result[1];
            value->z = result[2];
            return OPENUSD_STATUS_OK;
        });

    });
}
