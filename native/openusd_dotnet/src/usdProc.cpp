// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

openusd_status openusd_proc_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_proc_schema_kind schema_kind,
    int32_t* is_schema,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(is_schema);
        if (is_schema == nullptr || schema_kind != OPENUSD_PROC_SCHEMA_GENERATIVE_PROCEDURAL)
        {
            WriteError(error, "A valid UsdProc schema kind and result are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
            if (!prim)
            {
                return OPENUSD_STATUS_NOT_FOUND;
            }
            *is_schema = prim.IsA<UsdProcGenerativeProcedural>() ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_proc_define(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_proc_schema_kind schema_kind,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!IsValidPrimPath(prim_path) || schema_kind != OPENUSD_PROC_SCHEMA_GENERATIVE_PROCEDURAL)
        {
            WriteError(error, "A valid stage, absolute prim path, and UsdProc schema kind are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const UsdPrim prim = UsdProcGenerativeProcedural::Define(stage->value, SdfPath(prim_path)).GetPrim();
            if (!prim || !prim.IsA<UsdProcGenerativeProcedural>())
            {
                WriteError(error, "Could not define UsdProcGenerativeProcedural.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });
    });
}
