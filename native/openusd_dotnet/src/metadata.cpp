// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

openusd_status openusd_stage_set_prim_metadata(
    const openusd_stage* stage,
    const char* prim_path,
    const char* key,
    const openusd_metadata_value* value,
    const char* string_value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            key == nullptr || key[0] == '\0' || value == nullptr ||
            value->struct_size < sizeof(openusd_metadata_value))
        {
            WriteError(error, "A valid stage, prim path, key, and versioned value are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const VtValue vtValue = MakeMetadataValue(value, string_value);
            TfErrorMark mark;
            prim.SetCustomDataByKey(TfToken(key), vtValue);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the prim metadata." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_prim_metadata(
    const openusd_stage* stage,
    const char* prim_path,
    const char* key,
    int32_t requested_kind,
    openusd_metadata_value* value,
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
            key == nullptr || key[0] == '\0' || value == nullptr ||
            value->struct_size < sizeof(openusd_metadata_value))
        {
            WriteError(error, "A valid stage, prim path, key, and versioned value output are required.");
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

            const VtValue stored = prim.GetCustomDataByKey(TfToken(key));
            if (stored.IsEmpty())
            {
                WriteError(error, "The requested prim metadata was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            return ReadMetadataValue(
                stored, requested_kind, value, string_buffer, string_capacity, string_required, error);
        });

    });
}

openusd_status openusd_stage_clear_prim_metadata(
    const openusd_stage* stage,
    const char* prim_path,
    const char* key,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            key == nullptr || key[0] == '\0')
        {
            WriteError(error, "A valid stage, prim path, and key are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const TfToken keyToken(key);
            if (!prim.HasCustomDataKey(keyToken))
            {
                WriteError(error, "The requested prim metadata was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            TfErrorMark mark;
            prim.ClearCustomDataByKey(keyToken);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not clear the prim metadata." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}
