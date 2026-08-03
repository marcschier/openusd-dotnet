// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

openusd_status openusd_shade_is_material(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* is_material,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(is_material);
        if (stage == nullptr || !stage->value || is_material == nullptr ||
            !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage, absolute prim path, and result output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
        *is_material = prim && prim.IsA<UsdShadeMaterial>() ? 1 : 0;
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_is_shader(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* is_shader,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(is_shader);
        if (stage == nullptr || !stage->value || is_shader == nullptr ||
            !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage, absolute prim path, and result output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
        *is_shader = prim && prim.IsA<UsdShadeShader>() ? 1 : 0;
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_define_material(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage and absolute material prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const UsdShadeMaterial material =
                UsdShadeMaterial::Define(stage->value, SdfPath(prim_path));
            if (!material)
            {
                WriteError(error, "Could not define the UsdShadeMaterial prim.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_shade_define_shader(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage and absolute shader prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const UsdShadeShader shader =
                UsdShadeShader::Define(stage->value, SdfPath(prim_path));
            if (!shader)
            {
                WriteError(error, "Could not define the UsdShadeShader prim.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_shade_shader_set_source_id(
    const openusd_stage* stage,
    const char* shader_path,
    const char* source_id,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || source_id == nullptr ||
            source_id[0] == '\0' || !IsValidPrimPath(shader_path))
        {
            WriteError(error, "A valid stage, shader path, and source identifier are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdShadeShader shader(stage->value->GetPrimAtPath(SdfPath(shader_path)));
        if (!shader)
        {
            WriteError(error, "The requested prim is not a UsdShadeShader.");
            return OPENUSD_STATUS_NOT_FOUND;
        }
        if (!shader.SetShaderId(TfToken(source_id)))
        {
            WriteError(error, "Could not author the shader source identifier.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_shader_get_source_id(
    const openusd_stage* stage,
    const char* shader_path,
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
        if (stage == nullptr || !stage->value || required == nullptr ||
            !IsValidPrimPath(shader_path))
        {
            WriteError(error, "A valid stage, shader path, and size output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdShadeShader shader(stage->value->GetPrimAtPath(SdfPath(shader_path)));
        if (!shader)
        {
            WriteError(error, "The requested prim is not a UsdShadeShader.");
            return OPENUSD_STATUS_NOT_FOUND;
        }
        TfToken sourceId;
        if (!shader.GetShaderId(&sourceId))
        {
            WriteError(error, "The shader has no source identifier.");
            return OPENUSD_STATUS_NOT_FOUND;
        }
        return CopyString(sourceId.GetString(), buffer, capacity, required);

    });
}

openusd_status openusd_shade_create_input(
    const openusd_stage* stage,
    const char* connectable_path,
    const char* input_name,
    openusd_shade_value_type value_type,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || input_name == nullptr ||
            input_name[0] == '\0' || !IsValidPrimPath(connectable_path))
        {
            WriteError(error, "A valid stage, connectable path, and input name are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const SdfValueTypeName type = GetShadeValueType(value_type);
        if (!type)
        {
            WriteError(error, "The shader input value type is unsupported.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdShadeConnectableAPI connectable =
            GetRequiredConnectable(stage, connectable_path, error);
        if (!connectable)
        {
            return OPENUSD_STATUS_NOT_FOUND;
        }
        const UsdShadeInput existing = connectable.GetInput(TfToken(input_name));
        if (existing && existing.GetTypeName() != type)
        {
            WriteError(error, "An input with the requested name exists with a different type.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (!connectable.CreateInput(TfToken(input_name), type))
        {
            WriteError(error, "Could not create the shader input.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_get_input_type(
    const openusd_stage* stage,
    const char* connectable_path,
    const char* input_name,
    openusd_shade_value_type* value_type,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value_type);
        if (stage == nullptr || !stage->value || value_type == nullptr)
        {
            WriteError(error, "A valid stage and value-type output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdShadeInput input =
            GetRequiredShadeInput(stage, connectable_path, input_name, error);
        if (!input)
        {
            return IsValidPrimPath(connectable_path)
                ? OPENUSD_STATUS_NOT_FOUND
                : OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        *value_type = GetShadeValueType(input.GetTypeName());
        if (*value_type == OPENUSD_SHADE_VALUE_INVALID)
        {
            WriteError(error, "The shader input uses an unsupported value type.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_set_input_float(
    const openusd_stage* stage,
    const char* shader_path,
    const char* input_name,
    float value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return SetShadeInputValue(
            stage, shader_path, input_name, OPENUSD_SHADE_VALUE_FLOAT, value, error);

    });
}

openusd_status openusd_shade_get_input_float(
    const openusd_stage* stage,
    const char* shader_path,
    const char* input_name,
    float* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || value == nullptr)
        {
            WriteError(error, "A valid stage and float output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return GetShadeInputValue(
            stage, shader_path, input_name, OPENUSD_SHADE_VALUE_FLOAT, value, error);

    });
}

openusd_status openusd_shade_set_input_vec3f(
    const openusd_stage* stage,
    const char* shader_path,
    const char* input_name,
    openusd_shade_value_type value_type,
    openusd_vec3f value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value ||
            (value_type != OPENUSD_SHADE_VALUE_COLOR3F &&
             value_type != OPENUSD_SHADE_VALUE_VECTOR3F &&
             value_type != OPENUSD_SHADE_VALUE_NORMAL3F))
        {
            WriteError(error, "A valid stage and vec3-compatible shader value type are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return SetShadeInputValue(
            stage,
            shader_path,
            input_name,
            value_type,
            GfVec3f(value.x, value.y, value.z),
            error);

    });
}

openusd_status openusd_shade_get_input_vec3f(
    const openusd_stage* stage,
    const char* shader_path,
    const char* input_name,
    openusd_shade_value_type value_type,
    openusd_vec3f* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || value == nullptr ||
            (value_type != OPENUSD_SHADE_VALUE_COLOR3F &&
             value_type != OPENUSD_SHADE_VALUE_VECTOR3F &&
             value_type != OPENUSD_SHADE_VALUE_NORMAL3F))
        {
            WriteError(error, "A valid stage, vec3-compatible value type, and output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        GfVec3f nativeValue;
        const openusd_status status = GetShadeInputValue(
            stage, shader_path, input_name, value_type, &nativeValue, error);
        if (status == OPENUSD_STATUS_OK)
        {
            value->x = nativeValue[0];
            value->y = nativeValue[1];
            value->z = nativeValue[2];
        }
        return status;

    });
}

openusd_status openusd_shade_set_input_string(
    const openusd_stage* stage,
    const char* shader_path,
    const char* input_name,
    openusd_shade_value_type value_type,
    const char* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || value == nullptr ||
            (value_type != OPENUSD_SHADE_VALUE_TOKEN &&
             value_type != OPENUSD_SHADE_VALUE_STRING &&
             value_type != OPENUSD_SHADE_VALUE_ASSET))
        {
            WriteError(error, "A valid stage, string-like value type, and value are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (value_type == OPENUSD_SHADE_VALUE_TOKEN)
        {
            return SetShadeInputValue(
                stage, shader_path, input_name, value_type, TfToken(value), error);
        }
        if (value_type == OPENUSD_SHADE_VALUE_ASSET)
        {
            return SetShadeInputValue(
                stage, shader_path, input_name, value_type, SdfAssetPath(value), error);
        }
        return SetShadeInputValue(
            stage, shader_path, input_name, value_type, std::string(value), error);

    });
}

openusd_status openusd_shade_get_input_string(
    const openusd_stage* stage,
    const char* shader_path,
    const char* input_name,
    openusd_shade_value_type value_type,
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
        if (stage == nullptr || !stage->value || required == nullptr ||
            (value_type != OPENUSD_SHADE_VALUE_TOKEN &&
             value_type != OPENUSD_SHADE_VALUE_STRING &&
             value_type != OPENUSD_SHADE_VALUE_ASSET))
        {
            WriteError(error, "A valid stage, string-like value type, and size output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (value_type == OPENUSD_SHADE_VALUE_TOKEN)
        {
            TfToken value;
            const openusd_status status = GetShadeInputValue(
                stage, shader_path, input_name, value_type, &value, error);
            return status == OPENUSD_STATUS_OK
                ? CopyString(value.GetString(), buffer, capacity, required)
                : status;
        }
        if (value_type == OPENUSD_SHADE_VALUE_ASSET)
        {
            SdfAssetPath value;
            const openusd_status status = GetShadeInputValue(
                stage, shader_path, input_name, value_type, &value, error);
            return status == OPENUSD_STATUS_OK
                ? CopyString(value.GetAssetPath(), buffer, capacity, required)
                : status;
        }
        std::string value;
        const openusd_status status = GetShadeInputValue(
            stage, shader_path, input_name, value_type, &value, error);
        return status == OPENUSD_STATUS_OK
            ? CopyString(value, buffer, capacity, required)
            : status;

    });
}

openusd_status openusd_shade_create_output(
    const openusd_stage* stage,
    const char* connectable_path,
    const char* output_name,
    openusd_shade_value_type value_type,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || output_name == nullptr ||
            output_name[0] == '\0' || !IsValidPrimPath(connectable_path))
        {
            WriteError(error, "A valid stage, connectable path, and output name are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const SdfValueTypeName type = GetShadeValueType(value_type);
        if (!type)
        {
            WriteError(error, "The shader output value type is unsupported.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdShadeConnectableAPI connectable =
            GetRequiredConnectable(stage, connectable_path, error);
        if (!connectable)
        {
            return OPENUSD_STATUS_NOT_FOUND;
        }
        const UsdShadeOutput existing = connectable.GetOutput(TfToken(output_name));
        if (existing && existing.GetTypeName() != type)
        {
            WriteError(error, "An output with the requested name exists with a different type.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (!connectable.CreateOutput(TfToken(output_name), type))
        {
            WriteError(error, "Could not create the shader output.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_get_output_type(
    const openusd_stage* stage,
    const char* connectable_path,
    const char* output_name,
    openusd_shade_value_type* value_type,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value_type);
        if (stage == nullptr || !stage->value || value_type == nullptr)
        {
            WriteError(error, "A valid stage and value-type output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdShadeOutput output =
            GetRequiredShadeOutput(stage, connectable_path, output_name, error);
        if (!output)
        {
            return IsValidPrimPath(connectable_path)
                ? OPENUSD_STATUS_NOT_FOUND
                : OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        *value_type = GetShadeValueType(output.GetTypeName());
        if (*value_type == OPENUSD_SHADE_VALUE_INVALID)
        {
            WriteError(error, "The shader output uses an unsupported value type.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_connect(
    const openusd_stage* stage,
    const char* destination_path,
    const char* destination_name,
    openusd_shade_attribute_type destination_type,
    const char* source_path,
    const char* source_name,
    openusd_shade_attribute_type source_type,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value ||
            GetShadeAttributeType(destination_type) == UsdShadeAttributeType::Invalid ||
            GetShadeAttributeType(source_type) == UsdShadeAttributeType::Invalid)
        {
            WriteError(error, "A valid stage and input/output attribute kinds are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        const UsdShadeConnectableAPI source =
            GetRequiredConnectable(stage, source_path, error);
        if (!source)
        {
            return IsValidPrimPath(source_path)
                ? OPENUSD_STATUS_NOT_FOUND
                : OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const TfToken sourceName(source_name == nullptr ? "" : source_name);
        const UsdShadeInput sourceInput =
            source_type == OPENUSD_SHADE_ATTRIBUTE_INPUT
                ? source.GetInput(sourceName)
                : UsdShadeInput();
        const UsdShadeOutput sourceOutput =
            source_type == OPENUSD_SHADE_ATTRIBUTE_OUTPUT
                ? source.GetOutput(sourceName)
                : UsdShadeOutput();
        const SdfValueTypeName sourceValueType =
            sourceInput ? sourceInput.GetTypeName()
                        : sourceOutput ? sourceOutput.GetTypeName() : SdfValueTypeName();
        if (!sourceValueType)
        {
            WriteError(error, "The requested shading source attribute does not exist.");
            return OPENUSD_STATUS_NOT_FOUND;
        }

        if (destination_type == OPENUSD_SHADE_ATTRIBUTE_INPUT)
        {
            const UsdShadeInput destination =
                GetRequiredShadeInput(stage, destination_path, destination_name, error);
            if (!destination)
            {
                return IsValidPrimPath(destination_path)
                    ? OPENUSD_STATUS_NOT_FOUND
                    : OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            if (!AreShadeConnectionTypesCompatible(
                    sourceValueType, destination.GetTypeName()))
            {
                WriteError(error, "The source and destination shading types do not match.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            if (!destination.ConnectToSource(
                    source, sourceName, GetShadeAttributeType(source_type)))
            {
                WriteError(error, "Could not connect the shader input to the requested source.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        }

        const UsdShadeOutput destination =
            GetRequiredShadeOutput(stage, destination_path, destination_name, error);
        if (!destination)
        {
            return IsValidPrimPath(destination_path)
                ? OPENUSD_STATUS_NOT_FOUND
                : OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (!AreShadeConnectionTypesCompatible(
                sourceValueType, destination.GetTypeName()))
        {
            WriteError(error, "The source and destination shading types do not match.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (!destination.ConnectToSource(
                source, sourceName, GetShadeAttributeType(source_type)))
        {
            WriteError(error, "Could not connect the shader output to the requested source.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_disconnect(
    const openusd_stage* stage,
    const char* destination_path,
    const char* destination_name,
    openusd_shade_attribute_type destination_type,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        bool disconnected = false;
        if (destination_type == OPENUSD_SHADE_ATTRIBUTE_INPUT)
        {
            const UsdShadeInput input =
                GetRequiredShadeInput(stage, destination_path, destination_name, error);
            if (!input)
            {
                return IsValidPrimPath(destination_path)
                    ? OPENUSD_STATUS_NOT_FOUND
                    : OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            disconnected = input.DisconnectSource();
        }
        else if (destination_type == OPENUSD_SHADE_ATTRIBUTE_OUTPUT)
        {
            const UsdShadeOutput output =
                GetRequiredShadeOutput(stage, destination_path, destination_name, error);
            if (!output)
            {
                return IsValidPrimPath(destination_path)
                    ? OPENUSD_STATUS_NOT_FOUND
                    : OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            disconnected = output.DisconnectSource();
        }
        else
        {
            WriteError(error, "The destination must be a shading input or output.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (!disconnected)
        {
            WriteError(error, "Could not disconnect the shading property.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_get_connected_source(
    const openusd_stage* stage,
    const char* destination_path,
    const char* destination_name,
    openusd_shade_attribute_type destination_type,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_shade_attribute_type* source_type,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetStringListOutput(list, view);
        ResetAbiOutput(source_type);
        if (stage == nullptr || !stage->value || list == nullptr || view == nullptr ||
            source_type == nullptr || view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A valid stage and versioned connected-source outputs are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        const openusd_status status =
            GuardStringListOutput(error, list, view, [&](auto& result)
        {
            if (destination_type == OPENUSD_SHADE_ATTRIBUTE_INPUT)
            {
                const UsdShadeInput input =
                    GetRequiredShadeInput(stage, destination_path, destination_name, error);
                if (!input)
                {
                    return IsValidPrimPath(destination_path)
                        ? OPENUSD_STATUS_NOT_FOUND
                        : OPENUSD_STATUS_INVALID_ARGUMENT;
                }
                return GetConnectedShadeSource(input, result, view, source_type, error);
            }
            if (destination_type == OPENUSD_SHADE_ATTRIBUTE_OUTPUT)
            {
                const UsdShadeOutput output =
                    GetRequiredShadeOutput(stage, destination_path, destination_name, error);
                if (!output)
                {
                    return IsValidPrimPath(destination_path)
                        ? OPENUSD_STATUS_NOT_FOUND
                        : OPENUSD_STATUS_INVALID_ARGUMENT;
                }
                return GetConnectedShadeSource(output, result, view, source_type, error);
            }
            WriteError(error, "The destination must be a shading input or output.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        });
        if (status != OPENUSD_STATUS_OK)
        {
            ResetAbiOutput(source_type);
        }
        return status;

    });
}

openusd_status openusd_shade_get_connected_sources(
    const openusd_stage* stage,
    const char* destination_path,
    const char* destination_name,
    openusd_shade_attribute_type destination_type,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetStringListOutput(list, view);
        if (stage == nullptr || !stage->value || list == nullptr || view == nullptr ||
            view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A valid stage and ABI v2 connected-source outputs are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            if (destination_type == OPENUSD_SHADE_ATTRIBUTE_INPUT)
            {
                const UsdShadeInput input =
                    GetRequiredShadeInput(stage, destination_path, destination_name, error);
                return input
                    ? GetConnectedShadeSources(input, result, view, error)
                    : (IsValidPrimPath(destination_path)
                        ? OPENUSD_STATUS_NOT_FOUND
                        : OPENUSD_STATUS_INVALID_ARGUMENT);
            }
            if (destination_type == OPENUSD_SHADE_ATTRIBUTE_OUTPUT)
            {
                const UsdShadeOutput output =
                    GetRequiredShadeOutput(stage, destination_path, destination_name, error);
                return output
                    ? GetConnectedShadeSources(output, result, view, error)
                    : (IsValidPrimPath(destination_path)
                        ? OPENUSD_STATUS_NOT_FOUND
                        : OPENUSD_STATUS_INVALID_ARGUMENT);
            }
            WriteError(error, "The destination must be a shading input or output.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        });
    });
}

openusd_status openusd_shade_material_create_surface_output(
    const openusd_stage* stage,
    const char* material_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(material_path))
        {
            WriteError(error, "A valid stage and material path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdShadeMaterial material(stage->value->GetPrimAtPath(SdfPath(material_path)));
        if (!material)
        {
            WriteError(error, "The requested prim is not a UsdShadeMaterial.");
            return OPENUSD_STATUS_NOT_FOUND;
        }
        if (!material.CreateSurfaceOutput())
        {
            WriteError(error, "Could not create the material surface output.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_material_bind(
    const openusd_stage* stage,
    const char* prim_path,
    const char* material_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            !IsValidPrimPath(material_path))
        {
            WriteError(error, "A valid stage, prim path, and material path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
        if (!prim)
        {
            return OPENUSD_STATUS_NOT_FOUND;
        }
        const UsdShadeMaterial material(stage->value->GetPrimAtPath(SdfPath(material_path)));
        if (!material)
        {
            WriteError(error, "The requested material prim is missing or has the wrong schema.");
            return OPENUSD_STATUS_NOT_FOUND;
        }
        std::string whyNot;
        if (!UsdShadeMaterialBindingAPI::CanApply(prim, &whyNot))
        {
            WriteError(
                error,
                whyNot.empty() ? "MaterialBindingAPI cannot be applied to the prim." : whyNot);
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdShadeMaterialBindingAPI binding =
            UsdShadeMaterialBindingAPI::Apply(prim);
        if (!binding || !binding.Bind(material))
        {
            WriteError(error, "Could not bind the material to the prim.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_material_unbind(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage and prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
        if (!prim)
        {
            return OPENUSD_STATUS_NOT_FOUND;
        }
        const UsdShadeMaterialBindingAPI binding(prim);
        if (!binding || !binding.UnbindDirectBinding())
        {
            WriteError(error, "Could not remove the direct material binding.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_shade_get_direct_material(
    const openusd_stage* stage,
    const char* prim_path,
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
        if (stage == nullptr || !stage->value || required == nullptr ||
            !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage, prim path, and size output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
        if (!prim)
        {
            return OPENUSD_STATUS_NOT_FOUND;
        }
        const UsdShadeMaterial material =
            UsdShadeMaterialBindingAPI(prim).GetDirectBinding().GetMaterial();
        if (!material)
        {
            WriteError(error, "The prim has no directly bound material.");
            return OPENUSD_STATUS_NOT_FOUND;
        }
        return CopyString(
            material.GetPrim().GetPath().GetString(), buffer, capacity, required);

    });
}
