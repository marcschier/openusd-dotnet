// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

namespace
{
UsdVolVolume GetVolume(const openusd_stage* stage, const char* prim_path, openusd_error_buffer* error)
{
    const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
    UsdVolVolume volume(prim);
    if (prim && !volume)
    {
        WriteError(error, "The requested prim is not a UsdVolVolume.");
    }
    return volume;
}

bool IsVolSchema(const UsdPrim& prim, openusd_vol_schema_kind schema_kind)
{
    switch (schema_kind)
    {
        case OPENUSD_VOL_SCHEMA_VOLUME:
            return prim.IsA<UsdVolVolume>();
        case OPENUSD_VOL_SCHEMA_VOLUME_FIELD_BASE:
            return prim.IsA<UsdVolVolumeFieldBase>();
        case OPENUSD_VOL_SCHEMA_VOLUME_FIELD_ASSET:
            return prim.IsA<UsdVolVolumeFieldAsset>();
        case OPENUSD_VOL_SCHEMA_FIELD_BASE:
            return prim.IsA<UsdVolFieldBase>();
        case OPENUSD_VOL_SCHEMA_FIELD_ASSET:
            return prim.IsA<UsdVolFieldAsset>();
        case OPENUSD_VOL_SCHEMA_OPENVDB_ASSET:
            return prim.IsA<UsdVolOpenVDBAsset>();
        case OPENUSD_VOL_SCHEMA_FIELD3D_ASSET:
            return prim.IsA<UsdVolField3DAsset>();
        default:
            return false;
    }
}

UsdAttribute GetVolAssetAttribute(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_vol_asset_property property,
    bool create,
    openusd_error_buffer* error)
{
    if (property != OPENUSD_VOL_ASSET_FILE_PATH)
    {
        WriteError(error, "The requested UsdVol asset property is unsupported.");
        return {};
    }
    const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
    UsdVolVolumeFieldAsset asset(prim);
    if (!asset)
    {
        WriteError(error, "The requested prim is not a UsdVolVolumeFieldAsset.");
        return {};
    }
    return create ? asset.CreateFilePathAttr() : asset.GetFilePathAttr();
}

UsdVolVolumeFieldAsset GetVolumeFieldAsset(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
    UsdVolVolumeFieldAsset asset(prim);
    if (prim && !asset)
    {
        WriteError(error, "The requested prim is not a UsdVolVolumeFieldAsset.");
    }
    return asset;
}
}

openusd_status openusd_vol_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_vol_schema_kind schema_kind,
    int32_t* is_schema,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(is_schema);
        if (is_schema == nullptr || schema_kind < OPENUSD_VOL_SCHEMA_VOLUME ||
            schema_kind > OPENUSD_VOL_SCHEMA_FIELD3D_ASSET)
        {
            WriteError(error, "A valid UsdVol schema kind and result are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
            if (!prim)
            {
                return OPENUSD_STATUS_NOT_FOUND;
            }
            *is_schema = IsVolSchema(prim, schema_kind) ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_vol_define(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_vol_schema_kind schema_kind,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!IsValidPrimPath(prim_path) || schema_kind < OPENUSD_VOL_SCHEMA_VOLUME ||
            schema_kind > OPENUSD_VOL_SCHEMA_FIELD3D_ASSET)
        {
            WriteError(error, "A valid stage, absolute prim path, and UsdVol schema kind are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const SdfPath path(prim_path);
            UsdPrim prim;
            switch (schema_kind)
            {
                case OPENUSD_VOL_SCHEMA_VOLUME:
                    prim = UsdVolVolume::Define(stage->value, path).GetPrim();
                    break;
                case OPENUSD_VOL_SCHEMA_OPENVDB_ASSET:
                    prim = UsdVolOpenVDBAsset::Define(stage->value, path).GetPrim();
                    break;
                case OPENUSD_VOL_SCHEMA_FIELD3D_ASSET:
                    prim = UsdVolField3DAsset::Define(stage->value, path).GetPrim();
                    break;
                default:
                    WriteError(error, "The requested UsdVol schema is abstract and cannot be defined.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            if (!prim || !IsVolSchema(prim, schema_kind))
            {
                WriteError(error, "Could not define the requested UsdVol schema.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_vol_get_field_paths(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetStringListOutput(list, view);
        if (list == nullptr || view == nullptr || view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "String-list owner and view outputs are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return GuardStringListOutput(error, list, view, [&](std::unique_ptr<openusd_string_list>& result)
        {
            UsdVolVolume volume = GetVolume(stage, prim_path, error);
            if (!volume)
            {
                return OPENUSD_STATUS_NOT_FOUND;
            }
            std::vector<std::string> values;
            const UsdVolVolume::FieldMap fields = volume.GetFieldPaths();
            values.reserve(fields.size() * 2);
            for (const auto& field : fields)
            {
                values.push_back(field.first.GetString());
                values.push_back(field.second.GetString());
            }
            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), values, view);
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_vol_set_field_path(
    const openusd_stage* stage,
    const char* prim_path,
    const char* field_name,
    const char* target_prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (field_name == nullptr || field_name[0] == '\0' || !IsValidPrimPath(target_prim_path))
        {
            WriteError(error, "A field name and absolute target prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdVolVolume volume = GetVolume(stage, prim_path, error);
            if (!volume)
            {
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (!volume.CreateFieldRelationship(TfToken(field_name), SdfPath(target_prim_path)))
            {
                WriteError(error, "Could not author the UsdVol field relationship.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_vol_has_field_relationship(
    const openusd_stage* stage,
    const char* prim_path,
    const char* field_name,
    int32_t* has_relationship,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(has_relationship);
        if (field_name == nullptr || field_name[0] == '\0' || has_relationship == nullptr)
        {
            WriteError(error, "A field name and result are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdVolVolume volume = GetVolume(stage, prim_path, error);
            if (!volume)
            {
                return OPENUSD_STATUS_NOT_FOUND;
            }
            *has_relationship = volume.HasFieldRelationship(TfToken(field_name)) ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_vol_block_field_relationship(
    const openusd_stage* stage,
    const char* prim_path,
    const char* field_name,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (field_name == nullptr || field_name[0] == '\0')
        {
            WriteError(error, "A field name is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdVolVolume volume = GetVolume(stage, prim_path, error);
            if (!volume)
            {
                return OPENUSD_STATUS_NOT_FOUND;
            }
            volume.BlockFieldRelationship(TfToken(field_name));
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_vol_set_asset(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_vol_asset_property property,
    const char* asset_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (asset_path == nullptr)
        {
            WriteError(error, "An asset path is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdAttribute attribute = GetVolAssetAttribute(stage, prim_path, property, true, error);
            return attribute ? SetLuxAttribute(attribute, SdfAssetPath(asset_path), "UsdVol asset", error)
                             : OPENUSD_STATUS_INVALID_ARGUMENT;
        });
    });
}

openusd_status openusd_vol_get_asset(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_vol_asset_property property,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(required);
        return Guard(error, [&]()
        {
            UsdAttribute attribute = GetVolAssetAttribute(stage, prim_path, property, false, error);
            if (!attribute)
            {
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            SdfAssetPath value;
            openusd_status status = GetLuxAttribute(attribute, &value, "UsdVol asset", error);
            return status == OPENUSD_STATUS_OK
                ? CopyString(value.GetAssetPath(), buffer, capacity, required)
                : status;
        });
    });
}

openusd_status openusd_vol_set_field_index(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t field_index,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        return Guard(error, [&]()
        {
            UsdVolVolumeFieldAsset asset = GetVolumeFieldAsset(stage, prim_path, error);
            if (!asset)
            {
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            return SetLuxAttribute(asset.CreateFieldIndexAttr(), static_cast<int>(field_index), "UsdVol fieldIndex", error);
        });
    });
}

openusd_status openusd_vol_get_field_index(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* field_index,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(field_index);
        if (field_index == nullptr)
        {
            WriteError(error, "A fieldIndex output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            UsdVolVolumeFieldAsset asset = GetVolumeFieldAsset(stage, prim_path, error);
            if (!asset)
            {
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            int value = 0;
            const openusd_status status = GetLuxAttribute(asset.GetFieldIndexAttr(), &value, "UsdVol fieldIndex", error);
            if (status == OPENUSD_STATUS_OK)
            {
                *field_index = static_cast<int32_t>(value);
            }
            return status;
        });
    });
}
