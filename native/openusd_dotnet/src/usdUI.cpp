// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

namespace
{
bool IsUiSchema(const UsdPrim& prim, openusd_ui_schema_kind schema_kind)
{
    switch (schema_kind)
    {
        case OPENUSD_UI_SCHEMA_BACKDROP:
            return prim.IsA<UsdUIBackdrop>();
        case OPENUSD_UI_SCHEMA_NODE_GRAPH_NODE_API:
            return prim.HasAPI<UsdUINodeGraphNodeAPI>();
        case OPENUSD_UI_SCHEMA_SCENE_GRAPH_PRIM_API:
            return prim.HasAPI<UsdUISceneGraphPrimAPI>();
        default:
            return false;
    }
}

template <typename TApi>
openusd_status ApplyApi(const UsdPrim& prim, const char* label, openusd_error_buffer* error)
{
    if (prim.HasAPI<TApi>())
    {
        return OPENUSD_STATUS_OK;
    }
    std::string whyNot;
    if (!TApi::CanApply(prim, &whyNot))
    {
        WriteError(error, whyNot.empty() ? std::string(label) + " cannot be applied." : whyNot);
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (!TApi::Apply(prim))
    {
        WriteError(error, std::string("Could not apply ") + label + ".");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    return OPENUSD_STATUS_OK;
}
}

openusd_status openusd_ui_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_ui_schema_kind schema_kind,
    int32_t* is_schema,
    openusd_error_buffer* error)
{
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        ResetAbiOutput(is_schema);
        if (is_schema == nullptr || schema_kind < OPENUSD_UI_SCHEMA_BACKDROP ||
            schema_kind > OPENUSD_UI_SCHEMA_SCENE_GRAPH_PRIM_API)
        {
            WriteError(error, "A valid UsdUI schema kind and result are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
            if (!prim)
            {
                return OPENUSD_STATUS_NOT_FOUND;
            }
            *is_schema = IsUiSchema(prim, schema_kind) ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_ui_define(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_ui_schema_kind schema_kind,
    openusd_error_buffer* error)
{
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!IsValidPrimPath(prim_path) || schema_kind != OPENUSD_UI_SCHEMA_BACKDROP)
        {
            WriteError(error, "Only UsdUIBackdrop is a definable UsdUI schema.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const UsdPrim prim = UsdUIBackdrop::Define(stage->value, SdfPath(prim_path)).GetPrim();
            if (!prim || !prim.IsA<UsdUIBackdrop>())
            {
                WriteError(error, "Could not define UsdUIBackdrop.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_ui_apply_api(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_ui_schema_kind schema_kind,
    openusd_error_buffer* error)
{
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (schema_kind != OPENUSD_UI_SCHEMA_NODE_GRAPH_NODE_API &&
            schema_kind != OPENUSD_UI_SCHEMA_SCENE_GRAPH_PRIM_API)
        {
            WriteError(error, "The requested UsdUI schema is not an applied API schema.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
            if (!prim)
            {
                return OPENUSD_STATUS_NOT_FOUND;
            }
            return schema_kind == OPENUSD_UI_SCHEMA_NODE_GRAPH_NODE_API
                ? ApplyApi<UsdUINodeGraphNodeAPI>(prim, "UsdUINodeGraphNodeAPI", error)
                : ApplyApi<UsdUISceneGraphPrimAPI>(prim, "UsdUISceneGraphPrimAPI", error);
        });
    });
}
