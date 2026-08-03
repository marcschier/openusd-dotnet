// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

namespace
{
bool IsRenderSchema(const UsdPrim& prim, openusd_render_schema_kind schema_kind)
{
    switch (schema_kind)
    {
        case OPENUSD_RENDER_SCHEMA_SETTINGS_BASE:
            return prim.IsA<UsdRenderSettingsBase>();
        case OPENUSD_RENDER_SCHEMA_SETTINGS:
            return prim.IsA<UsdRenderSettings>();
        case OPENUSD_RENDER_SCHEMA_PRODUCT:
            return prim.IsA<UsdRenderProduct>();
        case OPENUSD_RENDER_SCHEMA_VAR:
            return prim.IsA<UsdRenderVar>();
        case OPENUSD_RENDER_SCHEMA_PASS:
            return prim.IsA<UsdRenderPass>();
        default:
            return false;
    }
}

UsdRenderSettingsBase GetSettingsBase(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
    UsdRenderSettingsBase settings(prim);
    if (prim && !settings)
    {
        WriteError(error, "The requested prim is not a UsdRenderSettingsBase.");
    }
    return settings;
}
}

openusd_status openusd_render_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_render_schema_kind schema_kind,
    int32_t* is_schema,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(is_schema);
        if (is_schema == nullptr || schema_kind < OPENUSD_RENDER_SCHEMA_SETTINGS_BASE ||
            schema_kind > OPENUSD_RENDER_SCHEMA_PASS)
        {
            WriteError(error, "A valid UsdRender schema kind and result are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
            if (!prim)
            {
                return OPENUSD_STATUS_NOT_FOUND;
            }
            *is_schema = IsRenderSchema(prim, schema_kind) ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_render_define(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_render_schema_kind schema_kind,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!IsValidPrimPath(prim_path) || schema_kind < OPENUSD_RENDER_SCHEMA_SETTINGS_BASE ||
            schema_kind > OPENUSD_RENDER_SCHEMA_PASS)
        {
            WriteError(error, "A valid stage, absolute prim path, and UsdRender schema kind are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const SdfPath path(prim_path);
            UsdPrim prim;
            switch (schema_kind)
            {
                case OPENUSD_RENDER_SCHEMA_SETTINGS:
                    prim = UsdRenderSettings::Define(stage->value, path).GetPrim();
                    break;
                case OPENUSD_RENDER_SCHEMA_PRODUCT:
                    prim = UsdRenderProduct::Define(stage->value, path).GetPrim();
                    break;
                case OPENUSD_RENDER_SCHEMA_VAR:
                    prim = UsdRenderVar::Define(stage->value, path).GetPrim();
                    break;
                case OPENUSD_RENDER_SCHEMA_PASS:
                    prim = UsdRenderPass::Define(stage->value, path).GetPrim();
                    break;
                default:
                    WriteError(error, "The requested UsdRender schema is abstract and cannot be defined.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            if (!prim || !IsRenderSchema(prim, schema_kind))
            {
                WriteError(error, "Could not define the requested UsdRender schema.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_render_set_resolution(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t width,
    int32_t height,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (width <= 0 || height <= 0)
        {
            WriteError(error, "Render resolution dimensions must be positive.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdRenderSettingsBase settings = GetSettingsBase(stage, prim_path, error);
            if (!settings)
            {
                return OPENUSD_STATUS_NOT_FOUND;
            }
            return SetLuxAttribute(
                settings.CreateResolutionAttr(),
                GfVec2i(width, height),
                "render resolution",
                error);
        });
    });
}

openusd_status openusd_render_get_resolution(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* width,
    int32_t* height,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(width);
        ResetAbiOutput(height);
        if (width == nullptr || height == nullptr)
        {
            WriteError(error, "Render resolution output pointers are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdRenderSettingsBase settings = GetSettingsBase(stage, prim_path, error);
            if (!settings)
            {
                return OPENUSD_STATUS_NOT_FOUND;
            }
            GfVec2i value;
            const openusd_status status =
                GetLuxAttribute(settings.GetResolutionAttr(), &value, "render resolution", error);
            if (status == OPENUSD_STATUS_OK)
            {
                *width = value[0];
                *height = value[1];
            }
            return status;
        });
    });
}

openusd_status openusd_render_set_data_window_ndc(
    const openusd_stage* stage,
    const char* prim_path,
    float min_x,
    float min_y,
    float max_x,
    float max_y,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!std::isfinite(min_x) || !std::isfinite(min_y) ||
            !std::isfinite(max_x) || !std::isfinite(max_y))
        {
            WriteError(error, "Render dataWindowNDC values must be finite.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdRenderSettingsBase settings = GetSettingsBase(stage, prim_path, error);
            if (!settings)
            {
                return OPENUSD_STATUS_NOT_FOUND;
            }
            return SetLuxAttribute(
                settings.CreateDataWindowNDCAttr(),
                GfVec4f(min_x, min_y, max_x, max_y),
                "render dataWindowNDC",
                error);
        });
    });
}

openusd_status openusd_render_get_data_window_ndc(
    const openusd_stage* stage,
    const char* prim_path,
    float* min_x,
    float* min_y,
    float* max_x,
    float* max_y,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(min_x);
        ResetAbiOutput(min_y);
        ResetAbiOutput(max_x);
        ResetAbiOutput(max_y);
        if (min_x == nullptr || min_y == nullptr || max_x == nullptr || max_y == nullptr)
        {
            WriteError(error, "Render dataWindowNDC output pointers are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdRenderSettingsBase settings = GetSettingsBase(stage, prim_path, error);
            if (!settings)
            {
                return OPENUSD_STATUS_NOT_FOUND;
            }
            GfVec4f value;
            const openusd_status status =
                GetLuxAttribute(settings.GetDataWindowNDCAttr(), &value, "render dataWindowNDC", error);
            if (status == OPENUSD_STATUS_OK)
            {
                *min_x = value[0];
                *min_y = value[1];
                *max_x = value[2];
                *max_y = value[3];
            }
            return status;
        });
    });
}
