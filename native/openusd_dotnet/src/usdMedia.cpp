// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

namespace
{
bool IsMediaSchema(const UsdPrim& prim, openusd_media_schema_kind schema_kind)
{
    switch (schema_kind)
    {
        case OPENUSD_MEDIA_SCHEMA_SPATIAL_AUDIO:
            return prim.IsA<UsdMediaSpatialAudio>();
        case OPENUSD_MEDIA_SCHEMA_ASSET_PREVIEWS_API:
            return prim.HasAPI<UsdMediaAssetPreviewsAPI>();
        default:
            return false;
    }
}

openusd_status ApplyAssetPreviews(const UsdPrim& prim, openusd_error_buffer* error)
{
    if (prim.HasAPI<UsdMediaAssetPreviewsAPI>())
    {
        return OPENUSD_STATUS_OK;
    }
    std::string whyNot;
    if (!UsdMediaAssetPreviewsAPI::CanApply(prim, &whyNot))
    {
        WriteError(error, whyNot.empty() ? "UsdMediaAssetPreviewsAPI cannot be applied." : whyNot);
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (!UsdMediaAssetPreviewsAPI::Apply(prim))
    {
        WriteError(error, "Could not apply UsdMediaAssetPreviewsAPI.");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    return OPENUSD_STATUS_OK;
}
}

openusd_status openusd_media_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_media_schema_kind schema_kind,
    int32_t* is_schema,
    openusd_error_buffer* error)
{
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        ResetAbiOutput(is_schema);
        if (is_schema == nullptr || schema_kind < OPENUSD_MEDIA_SCHEMA_SPATIAL_AUDIO ||
            schema_kind > OPENUSD_MEDIA_SCHEMA_ASSET_PREVIEWS_API)
        {
            WriteError(error, "A valid UsdMedia schema kind and result are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
            if (!prim)
            {
                return OPENUSD_STATUS_NOT_FOUND;
            }
            *is_schema = IsMediaSchema(prim, schema_kind) ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_media_define(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_media_schema_kind schema_kind,
    openusd_error_buffer* error)
{
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!IsValidPrimPath(prim_path) || schema_kind != OPENUSD_MEDIA_SCHEMA_SPATIAL_AUDIO)
        {
            WriteError(error, "Only UsdMediaSpatialAudio is a definable UsdMedia schema.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const UsdPrim prim = UsdMediaSpatialAudio::Define(stage->value, SdfPath(prim_path)).GetPrim();
            if (!prim || !prim.IsA<UsdMediaSpatialAudio>())
            {
                WriteError(error, "Could not define UsdMediaSpatialAudio.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_media_apply_api(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_media_schema_kind schema_kind,
    openusd_error_buffer* error)
{
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (schema_kind != OPENUSD_MEDIA_SCHEMA_ASSET_PREVIEWS_API)
        {
            WriteError(error, "The requested UsdMedia schema is not an applied API schema.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
            return prim ? ApplyAssetPreviews(prim, error) : OPENUSD_STATUS_NOT_FOUND;
        });
    });
}

openusd_status openusd_media_set_asset(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_media_asset_property property,
    const char* asset_path,
    openusd_error_buffer* error)
{
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (asset_path == nullptr)
        {
            WriteError(error, "An asset path is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
            if (!prim)
            {
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (property == OPENUSD_MEDIA_ASSET_FILE_PATH)
            {
                UsdMediaSpatialAudio audio(prim);
                if (!audio)
                {
                    WriteError(error, "The requested prim is not a UsdMediaSpatialAudio.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }
                return SetLuxAttribute(audio.CreateFilePathAttr(), SdfAssetPath(asset_path), "spatial audio file path", error);
            }
            if (property == OPENUSD_MEDIA_ASSET_DEFAULT_THUMBNAIL)
            {
                UsdMediaAssetPreviewsAPI api(prim);
                if (!api)
                {
                    WriteError(error, "UsdMediaAssetPreviewsAPI is not applied to the requested prim.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }
                api.SetDefaultThumbnails(UsdMediaAssetPreviewsAPI::Thumbnails(SdfAssetPath(asset_path)));
                return OPENUSD_STATUS_OK;
            }
            WriteError(error, "The requested UsdMedia asset property is unsupported.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        });
    });
}

openusd_status openusd_media_get_asset(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_media_asset_property property,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        ResetAbiOutput(required);
        return Guard(error, [&]()
        {
            const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
            if (!prim)
            {
                return OPENUSD_STATUS_NOT_FOUND;
            }
            SdfAssetPath value;
            if (property == OPENUSD_MEDIA_ASSET_FILE_PATH)
            {
                UsdMediaSpatialAudio audio(prim);
                if (!audio)
                {
                    WriteError(error, "The requested prim is not a UsdMediaSpatialAudio.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }
                const openusd_status status = GetLuxAttribute(audio.GetFilePathAttr(), &value, "spatial audio file path", error);
                return status == OPENUSD_STATUS_OK ? CopyString(value.GetAssetPath(), buffer, capacity, required) : status;
            }
            if (property == OPENUSD_MEDIA_ASSET_DEFAULT_THUMBNAIL)
            {
                UsdMediaAssetPreviewsAPI api(prim);
                UsdMediaAssetPreviewsAPI::Thumbnails thumbnails;
                if (!api || !api.GetDefaultThumbnails(&thumbnails))
                {
                    WriteError(error, "Could not read default thumbnails from UsdMediaAssetPreviewsAPI.");
                    return OPENUSD_STATUS_NOT_FOUND;
                }
                return CopyString(thumbnails.defaultImage.GetAssetPath(), buffer, capacity, required);
            }
            WriteError(error, "The requested UsdMedia asset property is unsupported.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        });
    });
}

openusd_status openusd_media_clear_asset(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_media_asset_property property,
    openusd_error_buffer* error)
{
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        return Guard(error, [&]()
        {
            const UsdPrim prim = GetRequiredPrim(stage, prim_path, error);
            if (!prim)
            {
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (property == OPENUSD_MEDIA_ASSET_FILE_PATH)
            {
                UsdMediaSpatialAudio audio(prim);
                if (!audio)
                {
                    WriteError(error, "The requested prim is not a UsdMediaSpatialAudio.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }
                audio.GetFilePathAttr().Clear();
                return OPENUSD_STATUS_OK;
            }
            if (property == OPENUSD_MEDIA_ASSET_DEFAULT_THUMBNAIL)
            {
                UsdMediaAssetPreviewsAPI api(prim);
                if (!api)
                {
                    WriteError(error, "UsdMediaAssetPreviewsAPI is not applied to the requested prim.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }
                api.ClearDefaultThumbnails();
                return OPENUSD_STATUS_OK;
            }
            WriteError(error, "The requested UsdMedia asset property is unsupported.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        });
    });
}
