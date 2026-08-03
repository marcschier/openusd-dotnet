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
}

openusd_status openusd_render_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_render_schema_kind schema_kind,
    int32_t* is_schema,
    openusd_error_buffer* error)
{
    return GuardStage(stage, error, [&]() -> openusd_status
    {
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
