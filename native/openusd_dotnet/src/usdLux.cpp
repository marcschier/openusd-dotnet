// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

openusd_status openusd_lux_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_schema_kind schema_kind,
    int32_t* is_schema,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(is_schema);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            is_schema == nullptr || schema_kind < OPENUSD_LUX_SCHEMA_DISTANT_LIGHT ||
            schema_kind > OPENUSD_LUX_SCHEMA_CYLINDER_LIGHT)
        {
            WriteError(error, "A valid stage, absolute light path, schema kind, and result are required.");
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
            *is_schema = IsLuxSchema(prim, schema_kind) ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_lux_define(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_schema_kind schema_kind,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            schema_kind < OPENUSD_LUX_SCHEMA_DISTANT_LIGHT ||
            schema_kind > OPENUSD_LUX_SCHEMA_CYLINDER_LIGHT)
        {
            WriteError(error, "A valid stage, absolute light path, and schema kind are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const SdfPath path(prim_path);
            UsdPrim prim;
            switch (schema_kind)
            {
                case OPENUSD_LUX_SCHEMA_DISTANT_LIGHT:
                    prim = UsdLuxDistantLight::Define(stage->value, path).GetPrim();
                    break;
                case OPENUSD_LUX_SCHEMA_SPHERE_LIGHT:
                    prim = UsdLuxSphereLight::Define(stage->value, path).GetPrim();
                    break;
                case OPENUSD_LUX_SCHEMA_RECT_LIGHT:
                    prim = UsdLuxRectLight::Define(stage->value, path).GetPrim();
                    break;
                case OPENUSD_LUX_SCHEMA_DISK_LIGHT:
                    prim = UsdLuxDiskLight::Define(stage->value, path).GetPrim();
                    break;
                case OPENUSD_LUX_SCHEMA_DOME_LIGHT:
                    prim = UsdLuxDomeLight::Define(stage->value, path).GetPrim();
                    break;
                case OPENUSD_LUX_SCHEMA_CYLINDER_LIGHT:
                    prim = UsdLuxCylinderLight::Define(stage->value, path).GetPrim();
                    break;
                default:
                    break;
            }
            if (!prim || !IsLuxSchema(prim, schema_kind))
            {
                WriteError(error, "Could not define the requested UsdLux light schema.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_lux_set_float(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_float_property property,
    float value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!std::isfinite(value))
        {
            WriteError(error, "A finite light value is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (property == OPENUSD_LUX_FLOAT_INTENSITY && value < 0.0F)
        {
            WriteError(error, "Light intensity must be non-negative.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (property == OPENUSD_LUX_FLOAT_COLOR_TEMPERATURE &&
            (value < 1000.0F || value > 10000.0F))
        {
            WriteError(error, "Light color temperature must be between 1000K and 10000K.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        switch (property)
        {
            case OPENUSD_LUX_FLOAT_INTENSITY:
                return SetLuxAttribute(light.CreateIntensityAttr(), value, "light intensity", error);
            case OPENUSD_LUX_FLOAT_EXPOSURE:
                return SetLuxAttribute(light.CreateExposureAttr(), value, "light exposure", error);
            case OPENUSD_LUX_FLOAT_DIFFUSE:
                return SetLuxAttribute(light.CreateDiffuseAttr(), value, "light diffuse multiplier", error);
            case OPENUSD_LUX_FLOAT_SPECULAR:
                return SetLuxAttribute(
                    light.CreateSpecularAttr(), value, "light specular multiplier", error);
            case OPENUSD_LUX_FLOAT_COLOR_TEMPERATURE:
                return SetLuxAttribute(
                    light.CreateColorTemperatureAttr(), value, "light color temperature", error);
            default:
                WriteError(error, "The requested light float property is unsupported.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

    });
}

openusd_status openusd_lux_get_float(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_float_property property,
    float* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (value == nullptr)
        {
            WriteError(error, "A light float output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        switch (property)
        {
            case OPENUSD_LUX_FLOAT_INTENSITY:
                return GetLuxAttribute(light.GetIntensityAttr(), value, "light intensity", error);
            case OPENUSD_LUX_FLOAT_EXPOSURE:
                return GetLuxAttribute(light.GetExposureAttr(), value, "light exposure", error);
            case OPENUSD_LUX_FLOAT_DIFFUSE:
                return GetLuxAttribute(light.GetDiffuseAttr(), value, "light diffuse multiplier", error);
            case OPENUSD_LUX_FLOAT_SPECULAR:
                return GetLuxAttribute(light.GetSpecularAttr(), value, "light specular multiplier", error);
            case OPENUSD_LUX_FLOAT_COLOR_TEMPERATURE:
                return GetLuxAttribute(
                    light.GetColorTemperatureAttr(), value, "light color temperature", error);
            default:
                WriteError(error, "The requested light float property is unsupported.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

    });
}

openusd_status openusd_lux_set_bool(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_bool_property property,
    int32_t value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        switch (property)
        {
            case OPENUSD_LUX_BOOL_ENABLE_COLOR_TEMPERATURE:
                return SetLuxAttribute(
                    light.CreateEnableColorTemperatureAttr(),
                    value != 0,
                    "enable color temperature",
                    error);
            case OPENUSD_LUX_BOOL_NORMALIZE:
                return SetLuxAttribute(light.CreateNormalizeAttr(), value != 0, "light normalize", error);
            default:
                WriteError(error, "The requested light bool property is unsupported.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

    });
}

openusd_status openusd_lux_get_bool(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_bool_property property,
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
            WriteError(error, "A light bool output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        bool nativeValue = false;
        openusd_status readStatus;
        switch (property)
        {
            case OPENUSD_LUX_BOOL_ENABLE_COLOR_TEMPERATURE:
                readStatus = GetLuxAttribute(
                    light.GetEnableColorTemperatureAttr(),
                    &nativeValue,
                    "enable color temperature",
                    error);
                break;
            case OPENUSD_LUX_BOOL_NORMALIZE:
                readStatus = GetLuxAttribute(
                    light.GetNormalizeAttr(), &nativeValue, "light normalize", error);
                break;
            default:
                WriteError(error, "The requested light bool property is unsupported.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (readStatus == OPENUSD_STATUS_OK)
        {
            *value = nativeValue ? 1 : 0;
        }
        return readStatus;

    });
}

openusd_status openusd_lux_set_color(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_vec3f value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!std::isfinite(value.x) || !std::isfinite(value.y) || !std::isfinite(value.z))
        {
            WriteError(error, "A finite light color is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &light, error);
        return status == OPENUSD_STATUS_OK
            ? SetLuxAttribute(
                light.CreateColorAttr(), GfVec3f(value.x, value.y, value.z), "light color", error)
            : status;

    });
}

openusd_status openusd_lux_get_color(
    const openusd_stage* stage,
    const char* prim_path,
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
            WriteError(error, "A light color output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        GfVec3f nativeValue;
        const openusd_status readStatus =
            GetLuxAttribute(light.GetColorAttr(), &nativeValue, "light color", error);
        if (readStatus == OPENUSD_STATUS_OK)
        {
            value->x = nativeValue[0];
            value->y = nativeValue[1];
            value->z = nativeValue[2];
        }
        return readStatus;

    });
}

openusd_status openusd_lux_set_shape(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_shape_property property,
    float value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!std::isfinite(value))
        {
            WriteError(error, "A finite light shape value is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (property == OPENUSD_LUX_SHAPE_ANGLE &&
            (value < 0.0F || value >= 360.0F))
        {
            WriteError(error, "Distant-light angle must be at least 0 and less than 360 degrees.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (property != OPENUSD_LUX_SHAPE_ANGLE && value < 0.0F)
        {
            WriteError(error, "Light dimensions and radii must be non-negative.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdPrim prim;
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &prim, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        const UsdAttribute attribute = GetLuxShapeAttribute(prim, property, true, error);
        return attribute
            ? SetLuxAttribute(attribute, value, "light shape property", error)
            : OPENUSD_STATUS_INVALID_ARGUMENT;

    });
}

openusd_status openusd_lux_get_shape(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_shape_property property,
    float* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (value == nullptr)
        {
            WriteError(error, "A light shape output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdPrim prim;
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &prim, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        const UsdAttribute attribute = GetLuxShapeAttribute(prim, property, false, error);
        return attribute
            ? GetLuxAttribute(attribute, value, "light shape property", error)
            : OPENUSD_STATUS_INVALID_ARGUMENT;

    });
}

openusd_status openusd_lux_set_asset(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_asset_property property,
    const char* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (value == nullptr)
        {
            WriteError(error, "A light asset path is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdPrim prim;
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &prim, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        const UsdAttribute attribute = GetLuxTextureAttribute(prim, property, true, error);
        return attribute
            ? SetLuxAttribute(attribute, SdfAssetPath(value), "light texture asset", error)
            : OPENUSD_STATUS_INVALID_ARGUMENT;

    });
}

openusd_status openusd_lux_get_asset(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_asset_property property,
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
            WriteError(error, "A light asset size output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdPrim prim;
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &prim, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        const UsdAttribute attribute = GetLuxTextureAttribute(prim, property, false, error);
        if (!attribute)
        {
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        SdfAssetPath value;
        const openusd_status readStatus =
            GetLuxAttribute(attribute, &value, "light texture asset", error);
        return readStatus == OPENUSD_STATUS_OK
            ? CopyString(value.GetAssetPath(), buffer, capacity, required)
            : readStatus;

    });
}

openusd_status openusd_lux_has_shaping(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* has_shaping,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(has_shaping);
        if (has_shaping == nullptr)
        {
            WriteError(error, "A shaping result output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdPrim prim;
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &prim, &light, error);
        if (status == OPENUSD_STATUS_OK)
        {
            *has_shaping = prim.HasAPI<UsdLuxShapingAPI>() ? 1 : 0;
        }
        return status;

    });
}

openusd_status openusd_lux_apply_shaping(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        UsdPrim prim;
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &prim, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        if (prim.HasAPI<UsdLuxShapingAPI>())
        {
            return OPENUSD_STATUS_OK;
        }
        std::string whyNot;
        if (!UsdLuxShapingAPI::CanApply(prim, &whyNot))
        {
            WriteError(error, whyNot.empty() ? "UsdLuxShapingAPI cannot be applied." : whyNot);
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (!UsdLuxShapingAPI::Apply(prim))
        {
            WriteError(error, "Could not apply UsdLuxShapingAPI.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_lux_set_shaping(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_shaping_property property,
    float value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!std::isfinite(value))
        {
            WriteError(error, "A finite shaping value is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (property == OPENUSD_LUX_SHAPING_FOCUS && value < 0.0F)
        {
            WriteError(error, "Light shaping focus must be non-negative.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (property == OPENUSD_LUX_SHAPING_CONE_ANGLE &&
            (value < 0.0F || value > 180.0F))
        {
            WriteError(error, "Light shaping cone angle must be between 0 and 180 degrees.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (property == OPENUSD_LUX_SHAPING_CONE_SOFTNESS &&
            (value < 0.0F || value > 1.0F))
        {
            WriteError(error, "Light shaping cone softness must be between 0 and 1.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdPrim prim;
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &prim, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        const UsdLuxShapingAPI shaping(prim);
        if (!shaping)
        {
            WriteError(error, "UsdLuxShapingAPI has not been applied to the light.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdAttribute attribute = GetLuxShapingAttribute(shaping, property, true, error);
        return attribute
            ? SetLuxAttribute(attribute, value, "light shaping property", error)
            : OPENUSD_STATUS_INVALID_ARGUMENT;

    });
}

openusd_status openusd_lux_get_shaping(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_shaping_property property,
    float* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (value == nullptr)
        {
            WriteError(error, "A shaping value output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        UsdPrim prim;
        UsdLuxLightAPI light;
        const openusd_status status = GetLuxLight(stage, prim_path, &prim, &light, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        const UsdLuxShapingAPI shaping(prim);
        if (!shaping)
        {
            WriteError(error, "UsdLuxShapingAPI has not been applied to the light.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const UsdAttribute attribute = GetLuxShapingAttribute(shaping, property, false, error);
        return attribute
            ? GetLuxAttribute(attribute, value, "light shaping property", error)
            : OPENUSD_STATUS_INVALID_ARGUMENT;

    });
}
