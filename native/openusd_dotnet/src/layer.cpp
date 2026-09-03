// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

void openusd_layer_release(openusd_layer* layer)
{
    ReleaseLayer(layer);
}

openusd_status openusd_layer_get_identifier(
    const openusd_layer* layer,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiStringOutput(buffer, capacity);
        ResetAbiOutput(required);
        if (layer == nullptr || !layer->value)
        {
            WriteError(error, "A valid layer handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            return CopyString(layer->value->GetIdentifier(), buffer, capacity, required);
        });

    });
}

openusd_status openusd_layer_save(
    const openusd_layer* layer,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]() -> openusd_status
    {
        if (layer == nullptr || !layer->value)
        {
            WriteError(error, "A valid layer handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const bool saved = layer->value->Save();
            if (!saved || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not save the layer." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_layer_reload(
    const openusd_layer* layer,
    int32_t force,
    int32_t* reloaded,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(reloaded);
        if (layer == nullptr || !layer->value || reloaded == nullptr)
        {
            WriteError(error, "A valid layer handle and reload output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const bool didReload = layer->value->Reload(force != 0);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not reload the layer." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            *reloaded = didReload ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_layer_export(
    const openusd_layer* layer,
    const char* path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]() -> openusd_status
    {
        if (layer == nullptr || !layer->value || path == nullptr || path[0] == '\0')
        {
            WriteError(error, "A valid layer and export path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const bool exported = layer->value->Export(path);
            if (!exported || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not export the layer." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_layer_add_sublayer(
    const openusd_layer* layer,
    const char* sublayer_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]() -> openusd_status
    {
        if (layer == nullptr || !layer->value || sublayer_path == nullptr || sublayer_path[0] == '\0')
        {
            WriteError(error, "A valid layer handle and sublayer path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            layer->value->InsertSubLayerPath(sublayer_path);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not add the sublayer." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_layer_remove_sublayer(
    const openusd_layer* layer,
    const char* sublayer_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]() -> openusd_status
    {
        if (layer == nullptr || !layer->value || sublayer_path == nullptr || sublayer_path[0] == '\0')
        {
            WriteError(error, "A valid layer handle and sublayer path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const SdfSubLayerProxy paths = layer->value->GetSubLayerPaths();
            int index = -1;
            for (size_t i = 0; i < paths.size(); ++i)
            {
                if (paths[i] == sublayer_path)
                {
                    index = static_cast<int>(i);
                    break;
                }
            }
            if (index < 0)
            {
                WriteError(error, "The requested sublayer was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            TfErrorMark mark;
            layer->value->RemoveSubLayerPath(index);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not remove the sublayer." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_layer_get_sublayer_paths(
    const openusd_layer* layer,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetStringListOutput(list, view);
        if (layer == nullptr || !layer->value || list == nullptr || view == nullptr ||
            view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A valid layer, list output, and versioned view are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            const SdfSubLayerProxy paths = layer->value->GetSubLayerPaths();
            const std::vector<std::string> values(paths.begin(), paths.end());
            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), values, view);
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_layer_set_metadata(
    const openusd_layer* layer,
    const char* key,
    const openusd_metadata_value* value,
    const char* string_value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]() -> openusd_status
    {
        if (layer == nullptr || !layer->value || key == nullptr || key[0] == '\0' ||
            value == nullptr || value->struct_size < sizeof(openusd_metadata_value))
        {
            WriteError(error, "A valid layer, key, and versioned value are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            VtDictionary data = layer->value->GetCustomLayerData();
            // Match UsdObject::SetCustomDataByKey's ':'-separated key-path semantics, so a
            // caller cannot get flat top-level keys on a layer and nested sub-dictionaries on
            // a prim from what looks like the same key.
            data.SetValueAtPath(std::string(key), MakeMetadataValue(value, string_value));
            layer->value->SetCustomLayerData(data);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the layer metadata." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_layer_get_metadata(
    const openusd_layer* layer,
    const char* key,
    int32_t requested_kind,
    openusd_metadata_value* value,
    char* string_buffer,
    size_t string_capacity,
    size_t* string_required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetVersionedAbiOutput(value);
        ResetAbiStringOutput(string_buffer, string_capacity);
        ResetAbiOutput(string_required);
        if (layer == nullptr || !layer->value || key == nullptr || key[0] == '\0' ||
            value == nullptr || value->struct_size < sizeof(openusd_metadata_value))
        {
            WriteError(error, "A valid layer, key, and versioned value output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const VtDictionary data = layer->value->GetCustomLayerData();
            const VtValue* stored = data.GetValueAtPath(std::string(key));
            if (stored == nullptr)
            {
                WriteError(error, "The requested layer metadata was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            return ReadMetadataValue(
                *stored,
                requested_kind,
                value,
                string_buffer,
                string_capacity,
                string_required,
                error);
        });

    });
}

openusd_status openusd_layer_clear_metadata(
    const openusd_layer* layer,
    const char* key,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardLayer(layer, error, [&]() -> openusd_status
    {
        if (layer == nullptr || !layer->value || key == nullptr || key[0] == '\0')
        {
            WriteError(error, "A valid layer and key are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            VtDictionary data = layer->value->GetCustomLayerData();
            if (data.GetValueAtPath(std::string(key)) == nullptr)
            {
                WriteError(error, "The requested layer metadata was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            TfErrorMark mark;
            data.EraseValueAtPath(std::string(key));
            layer->value->SetCustomLayerData(data);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not clear the layer metadata." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}
