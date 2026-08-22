// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

openusd_status openusd_stage_open(
    const char* path,
    openusd_stage** stage,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(stage);
        if (path == nullptr || stage == nullptr)
        {
            WriteError(error, "Stage path and output handle are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        *stage = nullptr;
        return Guard(error, [&]()
        {
            TfErrorMark mark;
            UsdStageRefPtr value = UsdStage::Open(path);
            if (!value || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                if (message.empty())
                {
                    message = std::string("Could not open stage: ") + path;
                }
                WriteError(error, message);
                return value ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }

            auto handle = std::make_unique<openusd_stage>(std::move(value));
            *stage = handle.release();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_open_masked(
    const char* path,
    const openusd_string_list_view* mask_paths,
    openusd_stage** stage,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(stage);
        if (path == nullptr || path[0] == '\0' || stage == nullptr)
        {
            WriteError(error, "Stage path, population mask, and output handle are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        *stage = nullptr;
        std::vector<SdfPath> paths;
        const openusd_status pathStatus = ReadAbsolutePrimPaths(mask_paths, &paths, error);
        if (pathStatus != OPENUSD_STATUS_OK)
        {
            return pathStatus;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const UsdStagePopulationMask mask(std::move(paths));
            UsdStageRefPtr value = UsdStage::OpenMasked(path, mask);
            if (!value || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                if (message.empty())
                {
                    message = std::string("Could not open masked stage: ") + path;
                }
                WriteError(error, message);
                return value ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }

            auto handle = std::make_unique<openusd_stage>(std::move(value));
            *stage = handle.release();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_create_new(
    const char* path,
    openusd_stage** stage,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(stage);
        if (path == nullptr || stage == nullptr)
        {
            WriteError(error, "Stage path and output handle are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        *stage = nullptr;
        return Guard(error, [&]()
        {
            TfErrorMark mark;
            UsdStageRefPtr value = UsdStage::CreateNew(path);
            if (!value || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                if (message.empty())
                {
                    message = std::string("Could not create stage: ") + path;
                }
                WriteError(error, message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            auto handle = std::make_unique<openusd_stage>(std::move(value));
            *stage = handle.release();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_retain(
    openusd_stage* stage,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (!RetainStageReference(stage))
        {
            WriteError(error, "A live stage handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return OPENUSD_STATUS_OK;
    });
}

void openusd_stage_release(openusd_stage* stage)
{
    ReleaseStageReference(stage);
}

openusd_status openusd_stage_access_begin(
    openusd_stage* stage,
    openusd_stage_access** access,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(access);
        if (access == nullptr || !RetainStageReference(stage))
        {
            WriteError(error, "A live stage and access output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        try
        {
            if (IsStageAccessBeginFailpoint("after-retain"))
            {
                throw std::bad_alloc();
            }
            auto guard = std::make_unique<openusd_stage_access>(stage);
            if (IsStageAccessBeginFailpoint("after-lock"))
            {
                throw std::runtime_error("Injected stage access begin failure after locking.");
            }
            *access = guard.release();
            return OPENUSD_STATUS_OK;
        }
        catch (...)
        {
            ReleaseStageReference(stage);
            throw;
        }
    });
}

openusd_status openusd_stage_access_end(
    openusd_stage_access* access,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    const openusd_status validation = Guard(error, [&]() -> openusd_status
    {
        if (access == nullptr || !access->lock.owns_lock())
        {
            WriteError(error, "A live stage access guard is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (access->owner != std::this_thread::get_id())
        {
            WriteError(error, "The stage access guard must end on its owner thread.");
            return OPENUSD_STATUS_WRONG_THREAD;
        }
        return OPENUSD_STATUS_OK;
    });
    if (validation != OPENUSD_STATUS_OK)
    {
        return validation;
    }

    FinalizeStageAccess(access);
    return OPENUSD_STATUS_OK;
}

openusd_status openusd_stage_get_root_layer(
    const openusd_stage* stage,
    openusd_layer** layer,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(layer);
        if (stage == nullptr || !stage->value || layer == nullptr)
        {
            WriteError(error, "A valid stage and layer output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        *layer = nullptr;
        return Guard(error, [&]()
        {
            auto handle = std::make_unique<openusd_layer>();
            handle->value = stage->value->GetRootLayer();
            if (!handle->value)
            {
                WriteError(error, "The stage has no root layer.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            if (!RetainStageReference(const_cast<openusd_stage*>(stage)))
            {
                WriteError(error, "The stage could not be retained for the root layer.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            handle->stage = const_cast<openusd_stage*>(stage);
            *layer = handle.release();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_session_layer(
    const openusd_stage* stage,
    openusd_layer** layer,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(layer);
        if (stage == nullptr || !stage->value || layer == nullptr)
        {
            WriteError(error, "A valid stage and layer output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        *layer = nullptr;
        return Guard(error, [&]()
        {
            auto handle = std::make_unique<openusd_layer>();
            handle->value = stage->value->GetSessionLayer();
            if (!handle->value)
            {
                WriteError(error, "The stage has no session layer.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (!RetainStageReference(const_cast<openusd_stage*>(stage)))
            {
                WriteError(error, "The stage could not be retained for the session layer.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            handle->stage = const_cast<openusd_stage*>(stage);
            *layer = handle.release();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_root_layer_identifier(
    const openusd_stage* stage,
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
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            return CopyString(stage->value->GetRootLayer()->GetIdentifier(), buffer, capacity, required);
        });

    });
}

openusd_status openusd_stage_get_session_layer_identifier(
    const openusd_stage* stage,
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
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const SdfLayerHandle layer = stage->value->GetSessionLayer();
            if (!layer)
            {
                WriteError(error, "The stage has no session layer.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            return CopyString(layer->GetIdentifier(), buffer, capacity, required);
        });

    });
}

openusd_status openusd_stage_get_edit_target_layer_identifier(
    const openusd_stage* stage,
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
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const SdfLayerHandle layer = stage->value->GetEditTarget().GetLayer();
            if (!layer)
            {
                WriteError(error, "The stage has no valid edit-target layer.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            return CopyString(layer->GetIdentifier(), buffer, capacity, required);
        });

    });
}

openusd_status openusd_stage_set_edit_target_root_layer(
    const openusd_stage* stage,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            return SetEditTargetLayer(stage, stage->value->GetRootLayer(), error);
        });

    });
}

openusd_status openusd_stage_set_edit_target_session_layer(
    const openusd_stage* stage,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            const SdfLayerHandle layer = stage->value->GetSessionLayer();
            if (!layer)
            {
                WriteError(error, "The stage has no session layer.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            return SetEditTargetLayer(stage, layer, error);
        });

    });
}

openusd_status openusd_stage_set_edit_target_layer(
    const openusd_stage* stage,
    const openusd_layer* layer,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || layer == nullptr || !layer->value ||
            layer->stage != stage)
        {
            WriteError(error, "A valid stage and owned layer handle are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return Guard(error, [&]()
        {
            return SetEditTargetLayer(stage, layer->value, error);
        });

    });
}

openusd_status openusd_stage_get_layer_stack_identifiers(
    const openusd_stage* stage,
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
            WriteError(error, "A valid stage and versioned string-list outputs are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            const SdfLayerHandleVector layers = stage->value->GetLayerStack(true);
            std::vector<std::string> identifiers;
            identifiers.reserve(layers.size());
            for (const SdfLayerHandle& layer : layers)
            {
                if (layer)
                {
                    identifiers.push_back(layer->GetIdentifier());
                }
            }

            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), identifiers, view);
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_mute_layer(
    const openusd_stage* stage,
    const char* layer_identifier,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value ||
            layer_identifier == nullptr || layer_identifier[0] == '\0')
        {
            WriteError(error, "A valid stage and layer identifier are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const std::string identifier(layer_identifier);
            if (stage->value->IsLayerMuted(identifier))
            {
                return OPENUSD_STATUS_OK;
            }
            const SdfLayerHandle layer = FindLayerInStack(stage, identifier);
            if (!layer)
            {
                WriteError(error, "The requested layer identifier was not found in the stage layer stack.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (layer == stage->value->GetRootLayer() || layer == stage->value->GetSessionLayer())
            {
                WriteError(error, "The stage root and session layers cannot be muted.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            TfErrorMark mark;
            stage->value->MuteLayer(identifier);
            if (!mark.IsClean() || !stage->value->IsLayerMuted(identifier))
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not mute the requested layer." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_unmute_layer(
    const openusd_stage* stage,
    const char* layer_identifier,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value ||
            layer_identifier == nullptr || layer_identifier[0] == '\0')
        {
            WriteError(error, "A valid stage and layer identifier are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const std::string identifier(layer_identifier);
            if (!stage->value->IsLayerMuted(identifier))
            {
                if (FindLayerInStack(stage, identifier))
                {
                    return OPENUSD_STATUS_OK;
                }
                WriteError(error, "The requested layer identifier was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            TfErrorMark mark;
            stage->value->UnmuteLayer(identifier);
            if (!mark.IsClean() || stage->value->IsLayerMuted(identifier))
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not unmute the requested layer." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_is_layer_muted(
    const openusd_stage* stage,
    const char* layer_identifier,
    int32_t* muted,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(muted);
        if (stage == nullptr || !stage->value ||
            layer_identifier == nullptr || layer_identifier[0] == '\0' || muted == nullptr)
        {
            WriteError(error, "A valid stage, layer identifier, and result output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const std::string identifier(layer_identifier);
            if (!IsKnownLayerIdentifier(stage, identifier))
            {
                WriteError(error, "The requested layer identifier was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            *muted = stage->value->IsLayerMuted(identifier) ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_save(
    const openusd_stage* stage,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const bool saved = stage->value->GetRootLayer()->Save();
            if (!saved || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not save the root layer." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_reload(
    const openusd_stage* stage,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            stage->value->Reload();
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not reload the stage." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_export(
    const openusd_stage* stage,
    const char* path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || path == nullptr || path[0] == '\0')
        {
            WriteError(error, "A valid stage and export path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const bool exported = stage->value->Export(path);
            if (!exported || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not export the stage." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_start_time_code(
    const openusd_stage* stage,
    double* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || value == nullptr)
        {
            WriteError(error, "A valid stage and value output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            *value = stage->value->GetStartTimeCode();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_start_time_code(
    const openusd_stage* stage,
    double value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !std::isfinite(value))
        {
            WriteError(error, "A valid stage and finite start time code are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            stage->value->SetStartTimeCode(value);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the start time code." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_end_time_code(
    const openusd_stage* stage,
    double* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || value == nullptr)
        {
            WriteError(error, "A valid stage and value output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            *value = stage->value->GetEndTimeCode();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_end_time_code(
    const openusd_stage* stage,
    double value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !std::isfinite(value))
        {
            WriteError(error, "A valid stage and finite end time code are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            stage->value->SetEndTimeCode(value);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the end time code." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_frames_per_second(
    const openusd_stage* stage,
    double* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || value == nullptr)
        {
            WriteError(error, "A valid stage and value output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            *value = stage->value->GetFramesPerSecond();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_frames_per_second(
    const openusd_stage* stage,
    double value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !std::isfinite(value) || value <= 0)
        {
            WriteError(error, "A valid stage and positive finite frames per second are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            stage->value->SetFramesPerSecond(value);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set frames per second." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_time_codes_per_second(
    const openusd_stage* stage,
    double* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (stage == nullptr || !stage->value || value == nullptr)
        {
            WriteError(error, "A valid stage and value output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            *value = stage->value->GetTimeCodesPerSecond();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_time_codes_per_second(
    const openusd_stage* stage,
    double value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !std::isfinite(value) || value <= 0)
        {
            WriteError(error, "A valid stage and positive finite time codes per second are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            stage->value->SetTimeCodesPerSecond(value);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set time codes per second." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_world_bounds(
    const openusd_stage* stage,
    const char* target_prim_path,
    uint32_t purpose_mask,
    int32_t time_sampled,
    double time_code,
    openusd_bounds3d* bounds,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        uint32_t struct_size = 0;
        uint32_t requested_version = 0;
        if (bounds != nullptr)
        {
            std::memcpy(&struct_size, bounds, sizeof(struct_size));
            if (struct_size >= offsetof(openusd_bounds3d, version) + sizeof(uint32_t))
            {
                std::memcpy(
                    &requested_version,
                    reinterpret_cast<const unsigned char*>(bounds) +
                        offsetof(openusd_bounds3d, version),
                    sizeof(requested_version));
            }
        }

        // ABI_OUTPUT_INITIALIZATION
        ResetBounds3dOutput(bounds);
        Bounds3dFailureReset failure_reset(bounds);
        const bool stage_bounds =
            target_prim_path == nullptr || target_prim_path[0] == '\0';
        if (stage == nullptr || !stage->value || bounds == nullptr ||
            !IsAligned(bounds) || struct_size < sizeof(openusd_bounds3d) ||
            requested_version != OPENUSD_BOUNDS3D_VERSION ||
            (!stage_bounds && !IsValidPrimPath(target_prim_path)) ||
            (purpose_mask & ~OPENUSD_GEOM_PURPOSE_MASK_ALL) != 0 ||
            (time_sampled != 0 && time_sampled != 1) ||
            (time_sampled == 1 && !std::isfinite(time_code)))
        {
            WriteError(
                error,
                "A valid stage, optional absolute prim path, purpose mask, time, "
                "and aligned bounds output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        if (purpose_mask == 0)
        {
            bounds->is_valid = 1;
            failure_reset.Commit();
            return OPENUSD_STATUS_OK;
        }

        TfErrorMark mark;
        const UsdPrim prim = stage_bounds
            ? stage->value->GetPseudoRoot()
            : stage->value->GetPrimAtPath(SdfPath(target_prim_path));
        if (!prim || !prim.IsActive())
        {
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(
                    error,
                    message.empty()
                        ? "Could not resolve the requested world-bounds prim."
                        : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            bounds->is_valid = 1;
            failure_reset.Commit();
            return OPENUSD_STATUS_OK;
        }

        TfTokenVector purposes;
        purposes.reserve(4);
        if ((purpose_mask & OPENUSD_GEOM_PURPOSE_MASK_DEFAULT) != 0)
        {
            purposes.push_back(UsdGeomTokens->default_);
        }
        if ((purpose_mask & OPENUSD_GEOM_PURPOSE_MASK_PROXY) != 0)
        {
            purposes.push_back(UsdGeomTokens->proxy);
        }
        if ((purpose_mask & OPENUSD_GEOM_PURPOSE_MASK_RENDER) != 0)
        {
            purposes.push_back(UsdGeomTokens->render);
        }
        if ((purpose_mask & OPENUSD_GEOM_PURPOSE_MASK_GUIDE) != 0)
        {
            purposes.push_back(UsdGeomTokens->guide);
        }
        GfRange3d range;
        {
            UsdGeomBBoxCache cache(
                GetTimeCode(time_sampled, time_code),
                std::move(purposes),
                true);
            range = cache.ComputeWorldBound(prim).ComputeAlignedRange();
        }
        if (!mark.IsClean())
        {
            std::string message = ConsumeErrors(mark);
            WriteError(
                error,
                message.empty() ? "Could not compute the requested world bounds." : message);
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        if (range.IsEmpty())
        {
            bounds->is_valid = 1;
            failure_reset.Commit();
            return OPENUSD_STATUS_OK;
        }

        const GfVec3d minimum = range.GetMin();
        const GfVec3d maximum = range.GetMax();
        for (size_t index = 0; index < 3; ++index)
        {
            if (!std::isfinite(minimum[index]) || !std::isfinite(maximum[index]) ||
                minimum[index] > maximum[index] ||
                !std::isfinite(maximum[index] - minimum[index]))
            {
                WriteError(error, "The computed world bounds are not finite and ordered.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            bounds->minimum[index] = minimum[index];
            bounds->maximum[index] = maximum[index];
        }
        bounds->is_valid = 1;
        bounds->is_empty = 0;
        failure_reset.Commit();
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_stage_get_world_oriented_bounds(
    const openusd_stage* stage,
    const char* target_prim_path,
    uint32_t purpose_mask,
    int32_t time_sampled,
    double time_code,
    openusd_oriented_bounds3d* bounds,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        uint32_t struct_size = 0;
        uint32_t requested_version = 0;
        if (bounds != nullptr)
        {
            std::memcpy(&struct_size, bounds, sizeof(struct_size));
            if (struct_size >= offsetof(openusd_oriented_bounds3d, version) + sizeof(uint32_t))
            {
                std::memcpy(
                    &requested_version,
                    reinterpret_cast<const unsigned char*>(bounds) +
                        offsetof(openusd_oriented_bounds3d, version),
                    sizeof(requested_version));
            }
        }

        // ABI_OUTPUT_INITIALIZATION
        ResetOrientedBounds3dOutput(bounds);
        OrientedBounds3dFailureReset failure_reset(bounds);
        const bool stage_bounds =
            target_prim_path == nullptr || target_prim_path[0] == '\0';
        if (stage == nullptr || !stage->value || bounds == nullptr ||
            !IsAligned(bounds) || struct_size < sizeof(openusd_oriented_bounds3d) ||
            requested_version != OPENUSD_ORIENTED_BOUNDS3D_VERSION ||
            (!stage_bounds && !IsValidPrimPath(target_prim_path)) ||
            (purpose_mask & ~OPENUSD_GEOM_PURPOSE_MASK_ALL) != 0 ||
            (time_sampled != 0 && time_sampled != 1) ||
            (time_sampled == 1 && !std::isfinite(time_code)))
        {
            WriteError(
                error,
                "A valid stage, optional absolute prim path, purpose mask, time, "
                "and aligned oriented-bounds output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        if (purpose_mask == 0)
        {
            bounds->is_valid = 1;
            failure_reset.Commit();
            return OPENUSD_STATUS_OK;
        }

        TfErrorMark mark;
        const UsdPrim prim = stage_bounds
            ? stage->value->GetPseudoRoot()
            : stage->value->GetPrimAtPath(SdfPath(target_prim_path));
        if (!prim || !prim.IsActive())
        {
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(
                    error,
                    message.empty()
                        ? "Could not resolve the requested world oriented-bounds prim."
                        : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            bounds->is_valid = 1;
            failure_reset.Commit();
            return OPENUSD_STATUS_OK;
        }

        TfTokenVector purposes;
        purposes.reserve(4);
        if ((purpose_mask & OPENUSD_GEOM_PURPOSE_MASK_DEFAULT) != 0)
        {
            purposes.push_back(UsdGeomTokens->default_);
        }
        if ((purpose_mask & OPENUSD_GEOM_PURPOSE_MASK_PROXY) != 0)
        {
            purposes.push_back(UsdGeomTokens->proxy);
        }
        if ((purpose_mask & OPENUSD_GEOM_PURPOSE_MASK_RENDER) != 0)
        {
            purposes.push_back(UsdGeomTokens->render);
        }
        if ((purpose_mask & OPENUSD_GEOM_PURPOSE_MASK_GUIDE) != 0)
        {
            purposes.push_back(UsdGeomTokens->guide);
        }

        GfBBox3d box;
        {
            UsdGeomBBoxCache cache(
                GetTimeCode(time_sampled, time_code),
                std::move(purposes),
                true);
            box = cache.ComputeWorldBound(prim);
        }
        if (!mark.IsClean())
        {
            std::string message = ConsumeErrors(mark);
            WriteError(
                error,
                message.empty() ? "Could not compute the requested world oriented bounds." : message);
            return OPENUSD_STATUS_NATIVE_ERROR;
        }

        const GfRange3d range = box.GetRange();
        if (range.IsEmpty())
        {
            bounds->is_valid = 1;
            failure_reset.Commit();
            return OPENUSD_STATUS_OK;
        }

        const GfVec3d minimum = range.GetMin();
        const GfVec3d maximum = range.GetMax();
        for (size_t index = 0; index < 3; ++index)
        {
            if (!std::isfinite(minimum[index]) || !std::isfinite(maximum[index]) ||
                minimum[index] > maximum[index] ||
                !std::isfinite(maximum[index] - minimum[index]))
            {
                WriteError(error, "The computed oriented bounds are not finite and ordered.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            bounds->minimum[index] = minimum[index];
            bounds->maximum[index] = maximum[index];
        }

        bounds->matrix = FromMatrix4d(box.GetMatrix());
        if (!IsFiniteMatrix(bounds->matrix))
        {
            WriteError(error, "The computed oriented bounds transform is not finite.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }

        bounds->is_valid = 1;
        bounds->is_empty = 0;
        failure_reset.Commit();
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_stage_get_default_prim_path(
    const openusd_stage* stage,
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
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetDefaultPrim();
            if (!prim)
            {
                WriteError(error, "The stage has no valid default prim.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            return CopyString(prim.GetPath().GetString(), buffer, capacity, required);
        });

    });
}

openusd_status openusd_stage_set_default_prim(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage and absolute prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, "The default prim must exist on the stage.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            TfErrorMark mark;
            stage->value->SetDefaultPrim(prim);
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the default prim." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_clear_default_prim(
    const openusd_stage* stage,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value)
        {
            WriteError(error, "A valid stage handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            stage->value->ClearDefaultPrim();
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not clear the default prim." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_define_prim(
    const openusd_stage* stage,
    const char* prim_path,
    const char* type_name,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage and absolute prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const TfToken type = type_name == nullptr ? TfToken() : TfToken(type_name);
            const UsdPrim prim = stage->value->DefinePrim(SdfPath(prim_path), type);
            if (!prim || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not define the prim." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_override_prim(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage and absolute prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const UsdPrim prim = stage->value->OverridePrim(SdfPath(prim_path));
            if (!prim || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not override the prim." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_create_class_prim(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        const SdfPath path(prim_path == nullptr ? "" : prim_path);
        if (stage == nullptr || !stage->value || !path.IsAbsolutePath() || !path.IsRootPrimPath())
        {
            WriteError(error, "A valid stage and absolute root prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const UsdPrim prim = stage->value->CreateClassPrim(path);
            if (!prim || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not create the class prim." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_prim_paths(
    const openusd_stage* stage,
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
            WriteError(error, "A valid stage, list output, and versioned view are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            std::vector<std::string> values;
            for (const UsdPrim& prim : stage->value->Traverse())
            {
                values.push_back(prim.GetPath().GetString());
            }

            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), values, view);
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_prim_path_statistics(
    const openusd_stage* stage,
    size_t maximum_prim_count,
    size_t maximum_total_path_bytes,
    size_t* prim_count,
    size_t* total_path_bytes,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(prim_count);
        ResetAbiOutput(total_path_bytes);
        if (stage == nullptr || !stage->value || prim_count == nullptr ||
            total_path_bytes == nullptr)
        {
            WriteError(error, "A valid stage and prim-path statistics outputs are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            size_t count = 0;
            size_t path_bytes = 0;
            TfErrorMark mark;
            for (const UsdPrim& prim : stage->value->Traverse())
            {
                const size_t current_path_bytes = prim.GetPath().GetString().size();
                if (count == std::numeric_limits<size_t>::max() ||
                    path_bytes > std::numeric_limits<size_t>::max() - current_path_bytes)
                {
                    count = std::numeric_limits<size_t>::max();
                    path_bytes = std::numeric_limits<size_t>::max();
                    break;
                }
                ++count;
                path_bytes += current_path_bytes;
                if (count > maximum_prim_count ||
                    path_bytes > maximum_total_path_bytes)
                {
                    break;
                }
            }
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(
                    error,
                    message.empty()
                        ? "Could not compute prim-path statistics."
                        : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            *prim_count = count;
            *total_path_bytes = path_bytes;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_prim_type_name(
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
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage and absolute prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, "The requested prim was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            return CopyString(prim.GetTypeName().GetString(), buffer, capacity, required);
        });

    });
}

openusd_status openusd_stage_get_prim_applied_schemas(
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
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            list == nullptr || view == nullptr ||
            view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A valid stage, prim path, list output, and versioned view are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, "The requested prim was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            std::vector<std::string> values;
            const TfTokenVector& schemas = prim.GetAppliedSchemas();
            values.reserve(schemas.size());
            for (const TfToken& schema : schemas)
            {
                values.push_back(schema.GetString());
            }

            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), values, view);
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_prim_child_paths(
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
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            list == nullptr || view == nullptr ||
            view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A valid stage, prim path, list output, and versioned view are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, "The requested prim was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            std::vector<std::string> values;
            for (const UsdPrim& child : prim.GetAllChildren())
            {
                values.push_back(child.GetPath().GetString());
            }

            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), values, view);
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_prim_attribute_names(
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
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            list == nullptr || view == nullptr ||
            view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A valid stage, prim path, list output, and versioned view are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, "The requested prim was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const std::vector<UsdAttribute> attributes = prim.GetAttributes();
            std::vector<std::string> values;
            values.reserve(attributes.size());
            for (const UsdAttribute& attribute : attributes)
            {
                values.push_back(attribute.GetName().GetString());
            }

            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), values, view);
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_prim_relationship_names(
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
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            list == nullptr || view == nullptr ||
            view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A valid stage, prim path, list output, and versioned view are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, "The requested prim was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const std::vector<UsdRelationship> relationships = prim.GetRelationships();
            std::vector<std::string> values;
            values.reserve(relationships.size());
            for (const UsdRelationship& relationship : relationships)
            {
                values.push_back(relationship.GetName().GetString());
            }

            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), values, view);
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_has_prim(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* exists,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(exists);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) || exists == nullptr)
        {
            WriteError(error, "A valid stage, prim path, and output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            *exists = prim ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_remove_prim(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage and absolute prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const bool removed = stage->value->RemovePrim(SdfPath(prim_path));
            if (!removed || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "The prim was not found." : message);
                return removed ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_set_prim_active(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t active,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage and absolute prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const bool set = prim.SetActive(active != 0);
            if (!set || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty() ? "Could not set the prim active state." : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_prim_active(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* active,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(active);
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) || active == nullptr)
        {
            WriteError(error, "A valid stage, prim path, and output are required.");
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
            *active = prim.IsActive() ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_prim_classification(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_prim_classification* classification,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        uint32_t struct_size = 0;
        uint32_t requested_version = 0;
        if (classification != nullptr)
        {
            std::memcpy(&struct_size, classification, sizeof(struct_size));
            if (struct_size >= offsetof(openusd_prim_classification, version) + sizeof(uint32_t))
            {
                std::memcpy(
                    &requested_version,
                    reinterpret_cast<const unsigned char*>(classification) +
                        offsetof(openusd_prim_classification, version),
                    sizeof(requested_version));
            }
        }

        // ABI_OUTPUT_INITIALIZATION
        ResetVersionedAbiOutput(classification);
        if (classification == nullptr || !IsAligned(classification) ||
            struct_size < sizeof(openusd_prim_classification) ||
            requested_version != OPENUSD_PRIM_CLASSIFICATION_VERSION ||
            stage == nullptr || !stage->value || !IsValidPrimPath(prim_path))
        {
            WriteError(error, "A valid stage, prim path, and prim-classification output are required.");
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

            classification->version = OPENUSD_PRIM_CLASSIFICATION_VERSION;
            classification->is_defined = prim.IsDefined() ? 1 : 0;
            classification->is_abstract = prim.IsAbstract() ? 1 : 0;
            classification->is_in_prototype = prim.IsInPrototype() ? 1 : 0;
            switch (prim.GetSpecifier())
            {
            case SdfSpecifierDef:
                classification->specifier = OPENUSD_PRIM_SPECIFIER_DEF;
                break;
            case SdfSpecifierOver:
                classification->specifier = OPENUSD_PRIM_SPECIFIER_OVER;
                break;
            case SdfSpecifierClass:
                classification->specifier = OPENUSD_PRIM_SPECIFIER_CLASS;
                break;
            default:
                classification->specifier = OPENUSD_PRIM_SPECIFIER_UNKNOWN;
                break;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_get_change_serial(
    const openusd_stage* stage,
    uint64_t* serial,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(serial);
        if (stage == nullptr || !stage->value || serial == nullptr)
        {
            WriteError(error, "A valid stage and serial output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        *serial = stage->change_serial.load(std::memory_order_relaxed);
        return OPENUSD_STATUS_OK;

    });
}
